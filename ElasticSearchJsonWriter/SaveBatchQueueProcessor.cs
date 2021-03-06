﻿using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using ElasticSearchApiCaller;
using ElasticSearchSqlFeeder.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ElasticSearchSqlFeeder.Shared;

namespace ElasticSearchJsonWriter
{
    public class SaveBatchQueueProcessor : BaseQueueProcessor<SaveBatchQueueItem, FileUploadQueueItem>
    {
        private static int _currentBatchFileNumber = 0;

        public SaveBatchQueueProcessor(QueueContext queueContext) : base(queueContext)
        {
        }


        protected override void Begin(bool isFirstThreadForThisTask)
        {
        }

        protected override void Complete(string queryId, bool isLastThreadForThisTask)
        {
        }

        protected override string GetId(SaveBatchQueueItem workitem)
        {
            return workitem.QueryId;
        }

        protected override void Handle(SaveBatchQueueItem workitem)
        {
            FlushDocumentsToBatchFile(workitem.ItemsToSave);
        }

        private void FlushDocumentsToBatchFile(IEnumerable<JsonObjectQueueItem> documentCacheItems)
        {
            var docs = documentCacheItems.Select(c => c.Document).ToList();

            var stream = new MemoryStream(); // do not use using since we'll pass it to next queue

            using (var textWriter = new StreamWriter(stream, Encoding.UTF8, 1024, true))
            using (var writer = new JsonTextWriter(textWriter))
            {
                foreach (var doc in docs)
                {
                    var entityId = doc[Config.TopLevelKeyColumn].Value<string>();

                    writer.WriteStartObject();
                    using (new JsonPropertyWrapper(writer, "update"))
                    {
                        writer.WritePropertyName("_id");
                        writer.WriteValue(entityId);
                    }
                    writer.WriteEndObject();
                    writer.WriteRaw("\n");

                    writer.WriteStartObject(); // <update>

                    //-- start writing doc
                    writer.WritePropertyName("doc");

                    doc.WriteTo(writer);
                    //writer.WriteRaw(doc.ToString());

                    // https://www.elastic.co/guide/en/elasticsearch/reference/current/docs-update.html
                    writer.WritePropertyName("doc_as_upsert");
                    writer.WriteValue(true);

                    writer.WriteEndObject(); // </update>

                    writer.WriteRaw("\n");
                }

            }

            var batchNumber = Interlocked.Increment(ref _currentBatchFileNumber);

            AddToOutputQueue(new FileUploadQueueItem
            {
                BatchNumber = batchNumber,
                Stream = stream
            });

            MyLogger.Trace($"Wrote batch: {batchNumber}");
        }

        protected override string LoggerName => "SaveBatch";


    }


    public class SaveBatchQueueItem : IQueueItem
    {
        public IEnumerable<JsonObjectQueueItem> ItemsToSave { get; set; }

        public string PropertyName { get; set; }

        public string QueryId { get; set; }
    }


}
