namespace Template.Functional.Tests.Common;

internal static class ApiRoutes
{
    internal static class V1
    {
        internal static class Account
        {
            internal const string Authenticate = "/api/v1/account/authenticate";
            internal const string ChangeUserName = "/api/v1/account/change-user-name";
            internal const string ChangePassword = "/api/v1/account/change-password";
            internal const string Start = "/api/v1/account/start";
        }

        internal static class Product
        {
            internal const string GetPagedListProduct = "/api/v1/product/get-paged-list";
            internal const string GetProductById = "/api/v1/product/get-by-id";
            internal const string CreateProduct = "/api/v1/product";
            internal const string UpdateProduct = "/api/v1/product";
            internal const string DeleteProduct = "/api/v1/product";
        }
    }
    internal static class V2
    {
        internal static class Account
        {
            internal const string Authenticate = "/api/v2/account/authenticate";
            internal const string ChangeUserName = "/api/v2/account/change-user-name";
            internal const string ChangePassword = "/api/v2/account/change-password";
            internal const string Start = "/api/v2/account/start";
        }

        internal static class Product
        {
            internal const string GetPagedListProduct = "/api/v2/product/get-paged-list";
            internal const string GetProductById = "/api/v2/product/get-by-id";
            internal const string CreateProduct = "/api/v2/product";
            internal const string UpdateProduct = "/api/v2/product";
            internal const string DeleteProduct = "/api/v2/product";
        }
    }
    internal static string AddQueryString(this string url, string key, string value)
    {
        var separator = url.Contains("?") ? "&" : "?";
        return $"{url}{separator}{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";
    }
}
