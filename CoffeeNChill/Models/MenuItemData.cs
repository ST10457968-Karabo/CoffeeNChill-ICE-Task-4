using System;
using System.Collections.Generic;
using System.Text;

namespace CoffeeNChill.Models
{
    public class MenuItemData
    {
        public string Category { get; set; }
        public string Stock { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public double Price { get; set; }
        public bool IsAvailable { get; set; }
    }
}
