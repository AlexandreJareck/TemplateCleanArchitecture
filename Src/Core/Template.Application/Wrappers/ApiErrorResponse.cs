namespace Template.Application.Wrappers
{
    public class ApiErrorResponse
    {
        public IEnumerable<ApiErrorItem> Errors { get; set; }
    }

    public class ApiErrorItem
    {
        public string Code { get; set; }
        public string Field { get; set; }
        public string Message { get; set; }
    }
}
