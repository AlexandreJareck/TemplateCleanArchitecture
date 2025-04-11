using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Template.WebApi.Service;

public interface IItemService
{
    Task<Item> GetItemAsync(int itemId);
}
public class ItemService : IItemService
{
    private readonly IDistributedCache _cache;
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);
    private readonly ILogger<ItemService> _logger;
    public ItemService(IDistributedCache cache, ILogger<ItemService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<Item> GetItemAsync(int itemId)
    {
        string cacheKey = $"Item_{itemId}";

        var cachedItem = await _cache.GetStringAsync(cacheKey);
        if (cachedItem != null)
        {
            _logger.LogInformation("Item retrieved from cache!");
            return JsonConvert.DeserializeObject<Item>(cachedItem);
        }

        var item = await FetchItemFromDatabase(itemId);

        await _cache.SetStringAsync(
            cacheKey,
            JsonConvert.SerializeObject(item),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
                SlidingExpiration = TimeSpan.FromMinutes(2) 
            }
        );

        _logger.LogInformation("🔄 Item added to cache.");
        return item;
    }

    private Task<Item> FetchItemFromDatabase(int itemId)
    {
        return Task.FromResult(new Item { Id = itemId, Name = "Sample Item", Description = "A cached item." });
    }
}

public class Item
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
}
