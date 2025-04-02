using Newtonsoft.Json;

namespace Template.WebApi.Models
{
    public class Person
    {
        [JsonProperty("Id")]
        public int Id { get; set; }

        [JsonProperty("FirstName")]
        public string? FirstName { get; set; }
        [JsonProperty("LastName")]
        public string? LastName { get; set; }
    }
}
