#nullable disable
using System;
using Template.Domain.Common;

namespace Template.Domain.Products.Entities
{
    public class ProductEntity : AuditableBaseEntity
    {
        private ProductEntity()
        {
        }
        public ProductEntity(string name, double price, string barCode)
        {
            Name = name;
            Price = price;
            BarCode = barCode;
        }

        public ProductEntity(Guid id, string name, double price, string barCode)
        {
            Id = id;
            Name = name;
            Price = price;
            BarCode = barCode;
        }
        public string Name { get; private set; }
        public double Price { get; private set; }
        public string BarCode { get; private set; }

        public void Update(string name, double price, string barCode)
        {
            Name = name;
            Price = price;
            BarCode = barCode;
        }
    }
}
