using Template.Domain.Products.Entities;

namespace Template.Application.DTOs.Products;

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
    public string Name { get; set; } = null!;
    public decimal Price { get; set; }
    public string BarCode { get; set; } = null!;
    public DateTime CreatedDateTime { get; set; }
}
