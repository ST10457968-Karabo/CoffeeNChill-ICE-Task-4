using Azure;
using Azure.Data.Tables;
using System;
using System.Collections.Generic;
using System.Text;

namespace CoffeeNChill.Models
{
    public class MenuItem : ITableEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string Name { get; set; }
        public string Description { get; set; }
        public double Price { get; set; } = 0;
        public bool IsAvailable { get; set; }
        ETag ITableEntity.ETag { get; set; }
    }
}
