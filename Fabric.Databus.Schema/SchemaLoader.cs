using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using ElasticSearchSqlFeeder.Interfaces;
using ElasticSearchSqlFeeder.Shared;
using Fabric.Databus.Config;

namespace Fabric.Databus.Schema
{
    public class SchemaLoader
    {
        public static List<MappingItem> GetSchemasForLoads(List<DataSource> workitemLoads, string connectionString,
            string topLevelKeyColumn)
        {
            var dictionary = new List<MappingItem>();

            foreach (var load in workitemLoads)
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    var cmd = conn.CreateCommand();
                    //cmd.CommandText = "SELECT TOP 10 * FROM [CatalystDevSubset].[dbo].[Patients]";

                    cmd.CommandText = $";WITH CTE AS ( {load.Sql} )  SELECT top 0 * from CTE;";

                    try
                    {
                        var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess);



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
                                IsJoinColumn = columnName.Equals(topLevelKeyColumn, StringComparison.OrdinalIgnoreCase),
                                ElasticSearchType = SqlTypeToElasticSearchTypeConvertor.GetElasticSearchType(columnType),
                                IsCalculated = false,
                            });
                        }

                        //var joinColumnIndex = columnList.FirstOrDefault(c => c.IsJoinColumn).index;

                        // add any calculated fields
                        var calculatedFields = load.Fields.Where(f => f.Destination != null)
                            .Select(f => new ColumnInfo
                            {
                                sourceIndex =
                                    columnList.FirstOrDefault(
                                            c => c.Name.Equals(f.Source, StringComparison.OrdinalIgnoreCase))?
                                        .index,
                                index = numberOfColumns++,
                                Name = f.Destination,
                                ElasticSearchType = f.DestinationType.ToString(),
                                IsCalculated = true,
                                Transform = f.Transform.ToString()
                            })
                            .ToList();

                        calculatedFields.ForEach(c => columnList.Add(c));


                        dictionary.Add(new MappingItem
                        {
                            SequenceNumber = load.SequenceNumber,
                            PropertyPath = load.Path,
                            PropertyType = load.PropertyType,
                            Columns = columnList,
                        });

                    }
                    catch (Exception e)
                    {
                        throw new ArgumentException($"Error in datasource (Path={load.Path}) with Sql:{cmd.CommandText}", e);
                    }
                }
            }
            return dictionary;
        }

    }

}