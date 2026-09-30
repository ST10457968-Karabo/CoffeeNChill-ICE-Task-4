using System.Net;
using Azure;
using Azure.Data.Tables;
using CoffeeNChill.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CoffeeNChill.Functions;

public class UpdateMenuItem
{
    private const string TableName = "MenuItems";

    private readonly ILogger<UpdateMenuItem> _logger;
    private readonly TableServiceClient _tableServiceClient;

    public UpdateMenuItem(ILogger<UpdateMenuItem> logger, TableServiceClient tableServiceClient)
    {
        _logger = logger;
        _tableServiceClient = tableServiceClient;
    }

    [Function("UpdateMenuItem")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "menu/{category}/{id}")] HttpRequestData req,
        string category,
        string id)
    {
        _logger.LogInformation($"UpdateMenuItem triggered for Category: {category}, Stock: {id}");

        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(id))
        {
            return await BuildErrorResponse(req, HttpStatusCode.BadRequest, "Category and Stock are required.");
        }

        UpdateMenuItemRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<UpdateMenuItemRequest>(
                req.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return await BuildErrorResponse(req, HttpStatusCode.BadRequest, "Request body is not valid JSON.");
        }

        if (body is null)
        {
            return await BuildErrorResponse(req, HttpStatusCode.BadRequest, "Request body is required.");
        }

        // Validates that at least one field that is up to date is provided
        if (!body.Price.HasValue && !body.IsAvailable.HasValue)
        {
            return await BuildErrorResponse(req, HttpStatusCode.BadRequest, "At least one of Price or IsAvailable must be provided for update.");
        }

        var tableClient = _tableServiceClient.GetTableClient(TableName);

        try
        {
            await tableClient.CreateIfNotExistsAsync();
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Could not access the MenuItems table.");
            return await BuildErrorResponse(req, HttpStatusCode.InternalServerError, "Could not access the menu items table.");
        }

        try
        {
            // Gets existing entity and if none exists display a 404 error message
            var response = await tableClient.GetEntityAsync<MenuItem>(category, id);

            if (!response.HasValue)
            {
                return req.CreateResponse(HttpStatusCode.NotFound);
            }

            var existingItem = response.Value;

            // Apply updates only if values are provided
            if (body.Price.HasValue)
            {
                if (body.Price.Value <= 0)
                {
                    return await BuildErrorResponse(req, HttpStatusCode.BadRequest, "Price must be greater than zero.");
                }
                existingItem.Price = body.Price.Value;
            }

            if (body.IsAvailable.HasValue)
            {
                existingItem.IsAvailable = body.IsAvailable.Value;
            }

            // Update the entity
            await tableClient.UpdateEntityAsync(existingItem, existingItem.ETag, TableUpdateMode.Replace);

            // Return updated entity
            var updatedDto = MenuItemMapper.ToDto(existingItem);
            var responseData = req.CreateResponse(HttpStatusCode.OK);
            await responseData.WriteAsJsonAsync(updatedDto);
            return responseData;
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            return req.CreateResponse(HttpStatusCode.NotFound);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, $"Error updating menu item {category}/{id}");
            return await BuildErrorResponse(req, HttpStatusCode.InternalServerError, "An error occurred while updating the menu item.");
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