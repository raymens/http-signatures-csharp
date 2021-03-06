﻿using HttpSignatures;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Headers;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http.Filters;

namespace AspNetActionFilter
{
    public class KeyStoreAuthenticationItem
    {
        public KeyStoreAuthenticationItem(string keyId, string secret, string[] roles)
        {
            this.KeyId = keyId;
            this.Secret = secret;
            this.Roles = roles;
        }

        public string KeyId { get; set; }

        public string Secret { get; set; }

        public string[] Roles { get; set; }
    }

    public interface IKeyStoreAuthenticationService
    {
        IDictionary<string, KeyStoreAuthenticationItem> GetKeyStore();
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class HttpSignatureAuthenticationAttribute : Attribute, IAuthenticationFilter
    {
        private string[] headers;

        public HttpSignatureAuthenticationAttribute(params string[] headers)
        {
            this.headers = headers;
        }

        public Task AuthenticateAsync(HttpAuthenticationContext authContext, System.Threading.CancellationToken cancellationToken)
        {
            var context = authContext.ActionContext;

            var keyService = (IKeyStoreAuthenticationService)context
                          .ControllerContext.Configuration.DependencyResolver
                          .GetService(typeof(IKeyStoreAuthenticationService));

            var keys = keyService.GetKeyStore();

            var request = context.Request;

            if (request.Headers.Authorization != null && request.Headers.Authorization.Scheme == "Signature")
            {
                var signer = new HttpSigner(new AuthorizationParser(), new HttpSignatureStringExtractor());

                var spec = new SignatureSpecification()
                {
                    Algorithm = "hmac-sha256",
                    Headers = headers,
                    //KeyId = "some-key" // TODO: make this dynamic..., seems like this can be omitted
                };

                var sigRequest = WebApiRequestConverter.FromHttpRequest(request);

                var store = keys.ToDictionary(t => t.Key, t => t.Value.Secret);

                var keyStore = new KeyStore(store);

                var signature = signer.Signature(sigRequest, spec, keyStore);

                var signatureString = new HttpSignatureStringExtractor().ExtractSignatureString(sigRequest, spec);

                Trace.WriteLine("Signature: " + signatureString);
                Trace.WriteLine("Expected: " + signature.ExpectedSignature);
                Trace.WriteLine("Received: " + request.Headers.Authorization.Parameter);

                if (signature.Valid)
                {
                    var roles = keys[signature.KeyId].Roles;

                    IPrincipal principal = new GenericPrincipal(new GenericIdentity(signature.KeyId), roles);

                    if (principal == null)
                    {
                        authContext.ErrorResult = new AuthenticationFailureResult("Invalid username or password", request);
                    }
                    else
                    {
                        authContext.Principal = principal;
                    }
                }
                else
                {
                    authContext.ErrorResult = new AuthenticationFailureResult("Invalid signature", request);
                }
            }

            return Task.FromResult(0);
        }

        public Task ChallengeAsync(HttpAuthenticationChallengeContext context, System.Threading.CancellationToken cancellationToken)
        {
            var challenge = new AuthenticationHeaderValue("Signature");
            context.Result = new AddChallengeOnUnauthorizedResult(challenge, context.Result);
            return Task.FromResult(0);
        }

        public bool AllowMultiple
        {
            get { throw new NotImplementedException(); }
        }
    }
}
