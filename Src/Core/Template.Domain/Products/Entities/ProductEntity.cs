using System;
using Template.Domain.Common;

namespace Template.Domain.Products.Entities;

public class ProductEntity : AuditableBaseEntity
{
    private ProductEntity()
    {
    }

    public ProductEntity(string name, decimal price, string barCode)
    {
        SetDetails(name, price, barCode);
    }

    public ProductEntity(Guid id, string name, decimal price, string barCode)
    {
        Id = id;
        SetDetails(name, price, barCode);
    }

    public string Name { get; private set; } = null!;
    public decimal Price { get; private set; }
    public string BarCode { get; private set; } = null!;

    public void Update(string name, decimal price, string barCode) => SetDetails(name, price, barCode);

    private void SetDetails(string name, decimal price, string barCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(price);
        ArgumentException.ThrowIfNullOrWhiteSpace(barCode);

        Name = name;
        Price = price;
        BarCode = barCode;
    }
}
