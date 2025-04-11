using Microsoft.AspNetCore.Mvc;
using Template.WebApi.Service;

namespace Template.WebApi.Controllers.v1;

[ApiController]
[ApiVersion("1")]
public class ItemsController : BaseApiController
{
    private readonly IItemService _itemService;

    public ItemsController(IItemService itemService)
    {
        _itemService = itemService;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        var item = await _itemService.GetItemAsync(id);
        return Ok(item);
    }
}
