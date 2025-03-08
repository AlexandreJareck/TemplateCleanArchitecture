using Template.Domain.Products.Entities;

namespace Template.Domain.Products.DTOs
{
    public class ProductDto
    {
        public ProductDto()
        {
                
        }

        public ProductDto(ProductEntity product)
        {
            Id = product.Id;
            Name = product.Name;
            Price = product.Price;
            BarCode = product.BarCode;
            CreatedDateTime = product.Created;
        }

        public Guid Id { get; set; }
        public string Name { get; set; }
        public double Price { get; set; }
        public string BarCode { get; set; }
        public DateTime CreatedDateTime { get; set; }
    }
}
