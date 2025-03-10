using Template.Domain.Products.Entities;
using System;

namespace Template.Domain.Products.DTOs
{
    public class ProductDto
    {
#pragma warning disable
        public ProductDto()
        {

        }
#pragma warning restore 
        public ProductDto(ProductEntity product)
        {
            Id = product.Id;
            Name = product.Name;
            Price = product.Price;
            BarCode = product.BarCode;
            CreatedDateTime = product.Created;
        }
        public long Id { get; set; }
        public string Name { get; set; }
        public double Price { get; set; }
        public string BarCode { get; set; }
        public DateTime CreatedDateTime { get; set; }
    }
}
