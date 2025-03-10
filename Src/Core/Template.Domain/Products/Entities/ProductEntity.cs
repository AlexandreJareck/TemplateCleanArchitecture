using Template.Domain.Common;

namespace Template.Domain.Products.Entities
{
    public class ProductEntity : AuditableBaseEntity
    {
#pragma warning disable
        private ProductEntity()
        {
        }
#pragma warning disable
        public ProductEntity(string name, double price, string barCode)
        {
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
