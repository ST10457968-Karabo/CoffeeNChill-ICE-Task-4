using System.Net;
using Azure;
using Azure.Data.Tables;
using CoffeeNChill.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace CoffeeNChill.Functions;

public class GetMenuItemsByCategory
{
    private const string TableName = "MenuItems";

    private readonly ILogger<GetMenuItemsByCategory> _logger;
    private readonly TableServiceClient _tableServiceClient;

    public GetMenuItemsByCategory(ILogger<GetMenuItemsByCategory> logger, TableServiceClient tableServiceClient)
    {
        _logger = logger;
        _tableServiceClient = tableServiceClient;
    }

    [Function("GetMenuItemsByCategory")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "menu/category/{category}")] HttpRequestData req,
        string category)
    {
        _logger.LogInformation($"GetMenuItemsByCategory triggered for category: {category}");

        if (string.IsNullOrWhiteSpace(category))
        {
            return await BuildErrorResponse(req, HttpStatusCode.BadRequest, "Category is required.");
        }

        var tableClient = _tableServiceClient.GetTableClient(TableName);

        try
        {
            await tableClient.CreateIfNotExistsAsync();
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex, "Could not confirm MenuItems table exists.");
            var emptyResponse = req.CreateResponse(HttpStatusCode.OK);
            await emptyResponse.WriteAsJsonAsync(Array.Empty<MenuItemData>());
            return emptyResponse;
        }

        try
        {
            // Query by PartitionKey (Category)
            var items = new List<MenuItemData>();
            await foreach (var entity in tableClient.QueryAsync<MenuItem>(
                filter: $"PartitionKey eq '{category}'"))
            {
                items.Add(MenuItemMapper.ToDto(entity));
            }

            // If no items are found, this will check if the category exists at all
            if (items.Count == 0)
            {
                // Check if there are any items that exist with this PartitionKey
                var categoryExists = false;
                await foreach (var entity in tableClient.QueryAsync<MenuItem>(
                    filter: $"PartitionKey eq '{category}'",
                    select: new[] { "PartitionKey" }))
                {
                    categoryExists = true;
                    break;
                }

                if (!categoryExists)
                {
                    // If the category doesn't exist it returns the error message 404 (NotFound)
                    return req.CreateResponse(HttpStatusCode.NotFound);
                }
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(items);
            return response;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, $"Error querying menu items for category: {category}");
            return await BuildErrorResponse(req, HttpStatusCode.InternalServerError, "An error occurred while retrieving menu items.");
        }
    }

    private static async Task<HttpResponseData> BuildErrorResponse(
        HttpRequestData req, HttpStatusCode status, string message)
    {
        var response = req.CreateResponse(status);
        await response.WriteAsJsonAsync(new { error = message });
        return response;
    }
}