﻿namespace Template.Application.DTOs.Account.Response
{
    public class AuthenticationResponse
    {
        public string Id { get; set; }
        public string UserName { get; set; }
        public string Email { get; set; }
        public IList<string> Roles { get; set; }
        public bool IsVerified { get; set; }
        public string JwToken { get; set; }
    }
}
