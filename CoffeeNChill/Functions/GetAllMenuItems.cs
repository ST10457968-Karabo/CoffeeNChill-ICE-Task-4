using System.Net;
using Azure;
using Azure.Data.Tables;
using CoffeeNChill.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace CoffeeNChill.Functions;

public class GetAllMenuItems
{
    private const string TableName = "MenuItems";

    private readonly ILogger<GetAllMenuItems> _logger;
    private readonly TableServiceClient _tableServiceClient;

    public GetAllMenuItems(ILogger<GetAllMenuItems> logger, TableServiceClient tableServiceClient)
    {
        _logger = logger;
        _tableServiceClient = tableServiceClient;
    }

    [Function("GetAllMenuItems")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "menu")] HttpRequestData req)
    {
        _logger.LogInformation("GetAllMenuItems triggered.");

        var tableClient = _tableServiceClient.GetTableClient(TableName);

        try
        {
            await tableClient.CreateIfNotExistsAsync();
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex, "Could not confirm MenuItems table exists; returning empty list.");
            var emptyResponse = req.CreateResponse(HttpStatusCode.OK);
            await emptyResponse.WriteAsJsonAsync(Array.Empty<MenuItemData>());
            return emptyResponse;
        }

        var items = new List<MenuItemData>();
        await foreach (var entity in tableClient.QueryAsync<MenuItem>())
        {
            items.Add(MenuItemMapper.ToDto(entity));
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(items); 
        return response;
    }
}