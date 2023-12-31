﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SCD2
{


    public class SCD2
    {
        public class DestTable
        {
            public string COMPANY_NAME { get; set; }
            public int? ID { get; set; }
            public DateTime? DELETION_DATE { get; set; }
            public int? IS_VALID { get; set; }
            public int? IS_CURRENT { get; set; }
            public DateTime? DT_VALID_FROM { get; set; }
            public DateTime? DT_VALID_TO { get; set; }
            public string CREATED_BY { get; set; }
            public DateTime? DT_CREATED { get; set; }
            public string MODIFIED_BY { get; set; }
            public DateTime? DT_MODIFIED { get; set; }
            public string PrimaryKeyHash { get; set; }

        }
        public class SourceTable
        {
            public string COMPANY_NAME { get; set; }
            public int? ID { get; set; }
            public DateTime? DELETION_DATE { get; set; }
        }
        private string connectionString = "Server=testing-sql.database.windows.net;Database=test;User Id=test;Password=test;";

        private string ComputeHash(SourceTable sourceRow)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                string input = sourceRow.COMPANY_NAME;
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                return BitConverter.ToString(hashBytes).Replace("-", string.Empty);
            }
        }

        public void PerformSCD2()
        {
            List<SourceTable> sourceData = GetSourceData();
            List<DestTable> destData = GetDestData();

            // Create a HashSet of hash values from the source data
            HashSet<string> sourceHashes = new HashSet<string>(sourceData.Select(s => ComputeHash(s)));

            // Create dictionaries for fast lookup based on hash values
            Dictionary<string, SourceTable> sourceLookup = sourceData.ToDictionary(s => ComputeHash(s));
            Dictionary<string, DestTable> destLookup = destData.ToDictionary(d => d.PrimaryKeyHash);

            List<SourceTable> recordsToInsert = new List<SourceTable>();
            List<SourceTable> recordsToUpdate = new List<SourceTable>();
            List<DestTable> recordsToDelete = new List<DestTable>();

            foreach (var destRow in destData)
            {
                if (!sourceHashes.Contains(destRow.PrimaryKeyHash))
                {
                    recordsToDelete.Add(destRow);
                }
            }

            foreach (var sourceRow in sourceData)
            {
                string sourceHash = ComputeHash(sourceRow);

                if (destLookup.TryGetValue(sourceHash, out var matchingDestRow))
                {
                    if (!AreRowsEqual(sourceRow, matchingDestRow))
                    {
                        recordsToUpdate.Add(sourceRow);
                    }
                }
                else
                {
                    recordsToInsert.Add(sourceRow);
                }
            }

            if (recordsToDelete.Any())
            {
                DeleteExistingRecord(recordsToDelete);
            }
            if (recordsToInsert.Any())
            {
                InsertNewRecords(recordsToInsert);
            }
            if (recordsToUpdate.Any())
            {
                UpdateExistingRecord(recordsToUpdate, destData);
            }
        }



        public List<SourceTable> GetSourceData()
        {
            List<SourceTable> sourceData = new List<SourceTable>();

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                string query = "SELECT * from dbo.Compnay";

                using (SqlCommand command = new SqlCommand(query, connection))
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        SourceTable sourceRow = new SourceTable
                        {
                            COMPANY_NAME = reader["COMPANY_NAME"].ToString(),
                            ID = reader["ID"] is DBNull ? (int?)null : (int)reader["ID"],
                            DELETION_DATE = reader["DELETION_DATE"] is DBNull ? (DateTime?)null : (DateTime)reader["DELETION_DATE"],
                        };

                        sourceData.Add(sourceRow);
                    }
                }
            }

            return sourceData;
        }


        private List<DestTable> GetDestData()
        {
            List<DestTable> destData = new List<DestTable>();

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                string query = "SELECT * FROM dbo.DIM_Compnay WHERE IS_CURRENT=1";

                using (SqlCommand command = new SqlCommand(query, connection))
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        DestTable destRow = new DestTable
                        {
                            COMPANY_NAME = reader["COMPANY_NAME"].ToString(),
                            ID = reader["ID"] is DBNull ? (int?)null : (int)reader["ID"],
                            DELETION_DATE = reader["DELETION_DATE"] is DBNull ? (DateTime?)null : (DateTime)reader["DELETION_DATE"],
                            IS_CURRENT = reader["IS_CURRENT"] is DBNull ? (int?)null : (int)reader["IS_CURRENT"],
                            IS_VALID = reader["IS_VALID"] is DBNull ? (int?)null : (int)reader["IS_VALID"],
                            DT_VALID_FROM = reader["DT_VALID_FROM"] is DBNull ? (DateTime?)null : (DateTime)reader["DT_VALID_FROM"],
                            DT_VALID_TO = reader["DT_VALID_TO"] is DBNull ? (DateTime?)null : (DateTime)reader["DT_VALID_TO"],
                            CREATED_BY = reader["CREATED_BY"].ToString(),
                            DT_CREATED = reader["DT_CREATED"] is DBNull ? (DateTime?)null : (DateTime)reader["DT_CREATED"],
                            MODIFIED_BY = reader["MODIFIED_BY"].ToString(),
                            DT_MODIFIED = reader["DT_MODIFIED"] is DBNull ? (DateTime?)null : (DateTime)reader["DT_MODIFIED"],
                            PrimaryKeyHash = ComputeHashFromColumns(reader["COMPANY_NAME"].ToString())
                        };

                        destData.Add(destRow);
                    }
                }
            }

            return destData;
        }

        private string ComputeHashFromColumns(string companyName)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                string input = companyName;
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                return BitConverter.ToString(hashBytes).Replace("-", string.Empty);
            }
        }
        private bool AreRowsEqual(SourceTable sourceRow, DestTable destRow)
        {
            return sourceRow.ID == destRow.ID &&
                   sourceRow.DELETION_DATE == destRow.DELETION_DATE;
        }


        private void UpdateExistingRecord(List<SourceTable> sourceRowsToUpdate, List<DestTable> matchingDestRows)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // Create a DataTable to hold the updated data
                DataTable updateTable = new DataTable();
                updateTable.Columns.Add("COMPANY_NAME", typeof(string));
                updateTable.Columns.Add("IS_CURRENT", typeof(int));
                updateTable.Columns.Add("DT_VALID_TO", typeof(DateTime));
                updateTable.Columns.Add("MODIFIED_BY", typeof(string));
                updateTable.Columns.Add("DT_MODIFIED", typeof(DateTime));

                List<SourceTable> recordsToInsert = new List<SourceTable>();
                foreach (var sourceRow in sourceRowsToUpdate)
                {
                    DataRow updateRow = updateTable.NewRow();
                    updateRow["COMPANY_NAME"] = sourceRow.COMPANY_NAME;
                    updateRow["IS_CURRENT"] = 0;
                    updateRow["DT_VALID_TO"] = DateTime.Now.AddDays(-1);
                    updateRow["MODIFIED_BY"] = "test@gmail.com";
                    updateRow["DT_MODIFIED"] = DateTime.Now;

                    updateTable.Rows.Add(updateRow);
                    recordsToInsert.Add(sourceRow);
                }

                if (updateTable.Rows.Count > 0)
                {
                    // Construct the SQL UPDATE statement with a JOIN clause
                    StringBuilder updateQuery = new StringBuilder();
                    updateQuery.Append("UPDATE D SET ");
                    updateQuery.Append("D.IS_CURRENT = U.IS_CURRENT, ");
                    updateQuery.Append("D.IS_VALID = CASE WHEN D.DT_VALID_FROM >= CONVERT(DATE, GETDATE()) THEN 0 ELSE D.IS_VALID END, ");
                    updateQuery.Append("D.DT_VALID_TO = U.DT_VALID_TO, ");
                    updateQuery.Append("D.MODIFIED_BY = U.MODIFIED_BY, ");
                    updateQuery.Append("D.DT_MODIFIED = U.DT_MODIFIED ");
                    updateQuery.Append("FROM dbo.DIM_Company AS D ");
                    updateQuery.Append("INNER JOIN @UpdateTable AS U ON D.COMPANY_NAME = U.COMPANY_NAME");
                    updateQuery.Append("WHERE D.IS_CURRENT = 1");

                    using (SqlCommand updateCommand = new SqlCommand(updateQuery.ToString(), connection))
                    {
                        SqlParameter updateTableParam = updateCommand.Parameters.AddWithValue("@UpdateTable", updateTable);
                        updateTableParam.SqlDbType = SqlDbType.Structured;
                        updateTableParam.TypeName = "DIM_Company_TYPE";

                        updateCommand.ExecuteNonQuery();
                    }
                }
                InsertNewRecords(recordsToInsert);
            }
        }

        private void DeleteExistingRecord(List<DestTable> matchingDestRows)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // Create a DataTable to hold the updated data
                DataTable updateTable = new DataTable();
                updateTable.Columns.Add("COMPANY_NAME", typeof(string)); //primary key column
                updateTable.Columns.Add("IS_CURRENT", typeof(int));
                updateTable.Columns.Add("DT_VALID_TO", typeof(DateTime));
                updateTable.Columns.Add("MODIFIED_BY", typeof(string));
                updateTable.Columns.Add("DT_MODIFIED", typeof(DateTime));

                foreach (var destRow in matchingDestRows)
                {
                    DataRow updateRow = updateTable.NewRow();
                    updateRow["COMPANY_NAME"] = destRow.COMPANY_NAME;  //primary key column
                    updateRow["ID"] = destRow.ID;  //primary key column
                    updateRow["IS_CURRENT"] = 0;
                    updateRow["DT_VALID_TO"] = DateTime.Now.AddDays(-1);
                    updateRow["MODIFIED_BY"] = "test@gmail.com";
                    updateRow["DT_MODIFIED"] = DateTime.Now;

                    updateTable.Rows.Add(updateRow);
                }

                if (updateTable.Rows.Count > 0)
                {
                    StringBuilder updateQuery = new StringBuilder();
                    updateQuery.Append("UPDATE D SET ");
                    updateQuery.Append("D.IS_CURRENT = U.IS_CURRENT, ");
                    updateQuery.Append("D.IS_VALID = CASE WHEN D.DT_VALID_FROM >= CONVERT(DATE, GETDATE()) THEN 0 ELSE D.IS_VALID END, ");
                    updateQuery.Append("D.DT_VALID_TO = U.DT_VALID_TO, ");
                    updateQuery.Append("D.MODIFIED_BY = U.MODIFIED_BY, ");
                    updateQuery.Append("D.DT_MODIFIED = U.DT_MODIFIED ");
                    updateQuery.Append("FROM dbo.DIM_Company AS D ");
                    updateQuery.Append("INNER JOIN @UpdateTable AS U ON D.COMPANY_NAME = U.COMPANY_NAME");
                    updateQuery.Append("WHERE D.IS_CURRENT = 1");

                    using (SqlCommand updateCommand = new SqlCommand(updateQuery.ToString(), connection))
                    {
                        SqlParameter updateTableParam = updateCommand.Parameters.AddWithValue("@UpdateTable", updateTable);
                        updateTableParam.SqlDbType = SqlDbType.Structured;
                        updateTableParam.TypeName = "DIM_Company_TYPE";

                        updateCommand.ExecuteNonQuery();
                    }
                }
            }
        }

        private void InsertNewRecords(List<SourceTable> sourceRows)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // Create a DataTable to hold the source data
                DataTable dataTable = new DataTable();
                dataTable.Columns.Add("COMPANY_NAME", typeof(string));
                dataTable.Columns.Add("ID", typeof(int));
                dataTable.Columns.Add("DELETION_DATE", typeof(DateTime));
                dataTable.Columns.Add("IS_VALID", typeof(int));
                dataTable.Columns.Add("IS_CURRENT", typeof(int));
                dataTable.Columns.Add("DT_VALID_FROM", typeof(DateTime));
                dataTable.Columns.Add("DT_VALID_TO", typeof(DateTime));
                dataTable.Columns.Add("CREATED_BY", typeof(string));
                dataTable.Columns.Add("DT_CREATED", typeof(DateTime));
                dataTable.Columns.Add("MODIFIED_BY", typeof(string));
                dataTable.Columns.Add("DT_MODIFIED", typeof(DateTime));

                // Populate the DataTable with the source data
                foreach (var sourceRow in sourceRows)
                {
                    dataTable.Rows.Add(
                        sourceRow.COMPANY_NAME,
                        sourceRow.ID,
                        sourceRow.DELETION_DATE,
                        1, 
                        1, 
                        DateTime.Now, 
                        new DateTime(9999, 12, 31),
                        "test@gmail.com",
                        DateTime.Now,
                        DBNull.Value,
                        DBNull.Value
                    );
                }

                // Create a SqlBulkCopy instance
                using (SqlBulkCopy bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, null))
                {
                    bulkCopy.DestinationTableName = "dbo.DIM_Company"; 
                    bulkCopy.BatchSize = 1000; 

                    foreach (DataColumn column in dataTable.Columns)
                    {
                        bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
                    }

                    bulkCopy.WriteToServer(dataTable);
                }
            }
        }

        public static void Main(string[] args)
        {
            SCD2 scd2 = new SCD2();
            scd2.PerformSCD2();

            Console.WriteLine("SCD2 process completed.");
        }

    }
}
