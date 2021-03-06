﻿using ElasticSearchJsonWriter;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using ElasticSearchSqlFeeder.Interfaces;
using ElasticSearchSqlFeeder.Shared;
using Fabric.Databus.Config;
using ZipCodeToGeoCodeConverter;

namespace SqlImporter
{
    public class SqlImportQueueProcessor : BaseQueueProcessor<SqlImportQueueItem, ConvertDatabaseToJsonQueueItem>
    {
        public SqlImportQueueProcessor(QueueContext queueContext)
            : base(queueContext)
        {
        }

        private void ReadOneQueryFromDatabase(string queryId, DataSource load, int seed, string start, string end, int workitemBatchNumber)
        {
            try
            {
                InternalReadOneQueryFromDatabase(queryId, load, start, end, workitemBatchNumber);
            }
            catch (Exception e)
            {
                throw new Exception($"Connection String: {Config.ConnectionString}", e);
            }
        }

        private void InternalReadOneQueryFromDatabase(string queryId, DataSource load, string start, string end, int batchNumber)
        {
            var sqlJsonValueWriter = new SqlJsonValueWriter();

            using (var conn = new SqlConnection(Config.ConnectionString))
            {
                conn.Open();
                var cmd = conn.CreateCommand();

                if (Config.SqlCommandTimeoutInSeconds != 0)
                    cmd.CommandTimeout = Config.SqlCommandTimeoutInSeconds;

                //cmd.CommandText = "SELECT TOP 10 * FROM [CatalystDevSubset].[dbo].[Patients]";

                cmd.CommandText =
                    $";WITH CTE AS ( {load.Sql} )  SELECT * from CTE WHERE {Config.TopLevelKeyColumn} BETWEEN '{start}' AND '{end}' ORDER BY {Config.TopLevelKeyColumn} ASC;";

                MyLogger.Trace($"Start: {cmd.CommandText}");
                var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess);


                //var schema = reader.GetSchemaTable();

                var numberOfColumns = reader.FieldCount;

                var columnList = new List<ColumnInfo>(numberOfColumns);

                for (int columnNumber = 0; columnNumber < numberOfColumns; columnNumber++)
                {
                    var columnName = reader.GetName(columnNumber);

                    var columnType = reader.GetFieldType(columnNumber);
                    columnList.Add(new ColumnInfo
                    {
                        index = columnNumber,
                        Name = columnName,
                        IsJoinColumn = columnName.Equals(Config.TopLevelKeyColumn, StringComparison.OrdinalIgnoreCase),
                        ElasticSearchType = SqlTypeToElasticSearchTypeConvertor.GetElasticSearchType(columnType),
                        IsCalculated = false,
                    });
                }

                var joinColumnIndex = columnList.FirstOrDefault(c => c.IsJoinColumn).index;

                // add any calculated fields
                var calculatedFields = load.Fields.Where(f => f.Destination != null)
                    .Select(f => new ColumnInfo
                    {
                        sourceIndex =
                            columnList.FirstOrDefault(c => c.Name.Equals(f.Source, StringComparison.OrdinalIgnoreCase))?.index,
                        index = numberOfColumns++,
                        Name = f.Destination,
                        ElasticSearchType = f.DestinationType.ToString(),
                        IsCalculated = true,
                        Transform = f.Transform.ToString()
                    })
                    .ToList();

                calculatedFields.ForEach(c => columnList.Add(c));

                //EsJsonWriter.WriteMappingToJson(columnList, load.PropertyPath);

                // now write the data
                var data = new Dictionary<string, List<object[]>>();

                var zipToGeocodeConverter = new ZipToGeocodeConverter();

                int rows = 0;

                while (reader.Read())
                {
                    rows++;
                    var values = new object[numberOfColumns];

                    reader.GetValues(values);

                    var key = Convert.ToString(values[joinColumnIndex]);
                    if (!data.ContainsKey(key))
                    {
                        data.Add(key, new List<object[]>());
                    }


                    foreach (var calculatedField in calculatedFields)
                    {
                        if (calculatedField.Transform != null && calculatedField.sourceIndex != null)
                        {
                            var sourceValue = values[calculatedField.sourceIndex.Value];
                            if (sourceValue != null)
                            {
                                if (calculatedField.Transform == QueryFieldTransform.Zip3ToGeocode.ToString())
                                {
                                    var sourceValueText = sourceValue.ToString();
                                    values[calculatedField.index] =
                                        zipToGeocodeConverter.Convert3DigitZipcodeToGeocode(sourceValueText);
                                }
                                if (calculatedField.Transform == QueryFieldTransform.Zip5ToGeocode.ToString())
                                {
                                    var sourceValueText = sourceValue.ToString();
                                    var convertZipcodeToGeocode = zipToGeocodeConverter.ConvertZipcodeToGeocode(sourceValueText);
                                    values[calculatedField.index] = convertZipcodeToGeocode;
                                }
                            }
                        }
                    }

                    data[key].Add(values);
                }

                MyLogger.Trace($"Finish: {cmd.CommandText} rows={rows}");

                foreach (var frame in data)
                {
                    AddToOutputQueue(new ConvertDatabaseToJsonQueueItem
                    {
                        BatchNumber = batchNumber,
                        QueryId = queryId,
                        JoinColumnValue = frame.Key,
                        Rows = frame.Value,
                        Columns = columnList,
                        PropertyName = load.Path,
                        PropertyType = load.PropertyType,
                        JsonValueWriter = sqlJsonValueWriter
                    });
                }

                //now all the source data has been loaded

                // handle fields without any transform
                var untransformedFields = load.Fields.Where(f => f.Transform == QueryFieldTransform.None)
                    .ToList();

                untransformedFields.ForEach(f => { });

                //EsJsonWriter.WriteRawDataToJson(data, columnList, seed, load.PropertyPath, 
                //    new SqlJsonValueWriter(), load.Index, load.EntityType);

                //esJsonWriter.WriteRawObjectsToJson(data, columnList, seed, load.PropertyPath, 
                //    new SqlJsonValueWriter(), load.Index, load.EntityType);
            }

            MyLogger.Trace($"Finished reading rows for {queryId}");
        }


        protected override void Handle(SqlImportQueueItem workitem)
        {
            ReadOneQueryFromDatabase(workitem.QueryId, workitem.DataSource, workitem.Seed, workitem.Start, workitem.End, workitem.BatchNumber);
        }

        protected override void Begin(bool isFirstThreadForThisTask)
        {
        }

        protected override void Complete(string queryId, bool isLastThreadForThisTask)
        {
            //MarkOutputQueueAsCompleted();
        }

        protected override string GetId(SqlImportQueueItem workitem)
        {
            return workitem.QueryId;
        }

        protected override string LoggerName => "SqlImport";
    }

    public class SqlImportQueueItem : IQueueItem
    {
        public string PropertyName { get; set; }

        public string QueryId { get; set; }

        public DataSource DataSource { get; set; }

        public int Seed { get; set; }
        public string Start { get; set; }
        public string End { get; set; }
        public int BatchNumber { get; set; }
    }
}
