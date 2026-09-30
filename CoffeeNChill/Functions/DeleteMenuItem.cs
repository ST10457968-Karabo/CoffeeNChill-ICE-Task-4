using System.Net;
using Azure;
using Azure.Data.Tables;
using CoffeeNChill.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace CoffeeNChill.Functions;

public class DeleteMenuItem
{
    private const string TableName = "MenuItems";

    private readonly ILogger<DeleteMenuItem> _logger;
    private readonly TableServiceClient _tableServiceClient;

    public DeleteMenuItem(ILogger<DeleteMenuItem> logger, TableServiceClient tableServiceClient)
    {
        _logger = logger;
        _tableServiceClient = tableServiceClient;
    }

    [Function("DeleteMenuItem")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "menu/{category}/{id}")] HttpRequestData req,
        string category,
        string id)
    {
        _logger.LogInformation($"DeleteMenuItem triggered for Category: {category}, Stock: {id}");

        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(id))
        {
            return await BuildErrorResponse(req, HttpStatusCode.BadRequest, "Category and Stock are required.");
        }

        var tableClient = _tableServiceClient.GetTableClient(TableName);

        try
        {
            await tableClient.CreateIfNotExistsAsync();
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Could not access MenuItems table.");
            return await BuildErrorResponse(req, HttpStatusCode.InternalServerError, "Could not access menu items table.");
        }

        try
        {
            // First check if the entity exists and if it doesn't exist dispaly a 404 error message
            var response = await tableClient.GetEntityAsync<MenuItem>(category, id);

            if (!response.HasValue)
            {
                return req.CreateResponse(HttpStatusCode.NotFound);
            }

            // If the entity exists, this deletes the entity
            await tableClient.DeleteEntityAsync(category, id, response.Value.ETag);

            // Return 204 (No Content) if entity was successfully deleted
            return req.CreateResponse(HttpStatusCode.NoContent);
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            return req.CreateResponse(HttpStatusCode.NotFound);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, $"Error deleting menu item {category}/{id}");
            return await BuildErrorResponse(req, HttpStatusCode.InternalServerError, "An error occurred while deleting the menu item.");
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