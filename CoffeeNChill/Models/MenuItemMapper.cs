using System;
using System.Collections.Generic;
using System.Text;

namespace CoffeeNChill.Models
{
    public static class MenuItemMapper
    {
        public static MenuItemData ToDto(MenuItem menuItem)
        {
            return new MenuItemData
            {
                Category = menuItem.PartitionKey,
                Stock = menuItem.RowKey,
                Name = menuItem.Name,
                Description = menuItem.Description,
                Price = menuItem.Price,
                IsAvailable = menuItem.IsAvailable
            };
        }
    }
}
        
