using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common;
using CSharpFunctionalExtensions;
using Marvin.StreamExtensions;
using Newtonsoft.Json;

namespace Movies.Client.Services
{
    public interface IApiService
    {
        Task<Result<T, ErrorResponse>> Get<T>(string url, Dictionary<string, string> queryParams) where T : class; 
        Task<Result<T, ErrorResponse>> Get<T>(string url) where T : class;
        Task<Result<T, ErrorResponse>> Get<T>(string url, Dictionary<string, string> queryParams, CancellationToken cancellationToken) where T : class;
        Task<Result> Post<T>(string uri, T data) where T : class;
        Task<Result> Post<T>(string uri, T data, CancellationToken cancellationToken) where T : class;
        Task<Result<T, ErrorResponse>> PostWithResult<T>(string uri, T data, CancellationToken cancellationToken) where T : class;
        Task<Result<T, ErrorResponse>> PostWithResult<T>(string uri, T data) where T : class;
        Task<Result<TR, ErrorResponse>> PostWithResult<T, TR>(string uri, T data, CancellationToken cancellationToken) where T : class where TR: class;
        Task<Result<TR, ErrorResponse>> PostWithResult<T, TR>(string uri, T data) where T : class where TR : class;
    }

    public class ApiService : IApiService
    {
        private readonly string httpClientName = "ApiClient";
        private readonly IHttpClientFactory httpClientFactory;

        public ApiService(IHttpClientFactory httpClientFactory)
        {
            this.httpClientFactory = httpClientFactory;
        }

        public async Task<Result<T, ErrorResponse>> Get<T>(string url, Dictionary<string, string> queryParams) where T : class
        {
            return await Get<T>(url, queryParams, CancellationToken.None);
        }

        public async Task<Result<T, ErrorResponse>> Get<T>(string url) where T : class
        {
            return await Get<T>(url, new Dictionary<string, string>(), CancellationToken.None);
        }

        public async Task<Result<T, ErrorResponse>> Get<T>(string url, Dictionary<string, string> queryParams, CancellationToken cancellationToken) where T : class
        {
            var httpClient = httpClientFactory.CreateClient(httpClientName);

            string requestUri = CreateGetRequestUri(url, queryParams);

            using (var requestMessage = Create(HttpMethod.Get, requestUri))
            {
                using (var responseMessage = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    return await ProcessResponse<T>(responseMessage);
                }
            }
        }

        public async Task<Result> Post<T>(string uri, T data) where T : class
        {
            var result = await Post(uri, data, CancellationToken.None);
            if (result.IsFailure)
            {
                return Result.Failure(result.Error);
            }

            return Result.Success();
        }

        public async Task<Result> Post<T>(string uri, T data, CancellationToken cancellationToken) where T : class
        {
            var result = await PostWithResult<T,T>(uri, data, cancellationToken);
            if (result.IsFailure)
            {
                return Result.Failure(result.Error.ToString());
            }

            return Result.Success();
        }

        public async Task<Result<T, ErrorResponse>> PostWithResult<T>(string uri, T data, CancellationToken cancellationToken) where T : class
        {
            return await PostWithResult<T, T>(uri, data, cancellationToken);
        }

        public async Task<Result<T, ErrorResponse>> PostWithResult<T>(string uri, T data) where T : class
        {
            return await PostWithResult<T>(uri, data, CancellationToken.None);
        }

        public async Task<Result<TR, ErrorResponse>> PostWithResult<T, TR>(string uri, T data) where T : class where TR : class
        {
            return await PostWithResult<T, TR>(uri, data, CancellationToken.None);
        }

        public async Task<Result<TR, ErrorResponse>> PostWithResult<T, TR>(string uri, T data, CancellationToken cancellationToken) where T : class where TR : class
        {
            var httpClient = httpClientFactory.CreateClient(httpClientName);

            var serializedData = JsonConvert.SerializeObject(new Request<T>(data));

            using (var requestMessage = Create(HttpMethod.Post, uri))
            {
                requestMessage.Content = new StringContent(serializedData);
                requestMessage.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                using (var responseMessage = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    return await ProcessResponse<TR>(responseMessage);
                }
            }
        }

        private async Task<Result<T, ErrorResponse>> PostWithStreams<T>(string uri, T data) where T : class
        {
            var httpClient = httpClientFactory.CreateClient(httpClientName);

            var memoryContentStream = new MemoryStream();
            memoryContentStream.SerializeToJsonAndWrite(new Request<T>(data), new UTF8Encoding(), 1024, true);

            memoryContentStream.Seek(0, SeekOrigin.Begin);
            using (var requestMessage = Create(HttpMethod.Post, uri))
            {
                using (var streamContent = new StreamContent(memoryContentStream))
                {
                    requestMessage.Content = streamContent;
                    requestMessage.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                    using (var responseMessage = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead))
                    {
                        return await ProcessResponse<T>(responseMessage);
                    }
                }
            }
        }

        private HttpRequestMessage Create(HttpMethod method, string uri)
        {
            var requestMessage = new HttpRequestMessage(method, uri);
            requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            requestMessage.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));

            return requestMessage;
        }

        private string CreateGetRequestUri(string url, Dictionary<string, string> queryParams)
        {
            IEnumerable<string> values = queryParams.Select(kvp => $"{kvp.Key}={kvp.Value}");
            string queryStringsPart = string.Join('&', values);
            string requestUri = string.IsNullOrEmpty(queryStringsPart) ? url : $"{url}?{queryStringsPart}";

            return requestUri;
        }

        private async Task<Result<T, ErrorResponse>> ProcessResponse<T>(HttpResponseMessage responseMessage) where T : class
        {
            if (!responseMessage.IsSuccessStatusCode)
            {
                var errorStream = await responseMessage.Content.ReadAsStreamAsync();
                
                Response<ErrorMessages> error = errorStream.ReadAndDeserializeFromJson<Response<ErrorMessages>>();
                return error?.Data != null
                    ? Result.Failure<T, ErrorResponse>(new ErrorResponse(responseMessage.StatusCode, error.Data.Messages))
                    : Result.Failure<T, ErrorResponse>(new ErrorResponse(responseMessage.StatusCode));
            }
            else
            {
                var stream = await responseMessage.Content.ReadAsStreamAsync();
                var result = stream.ReadAndDeserializeFromJson<Response<T>>();

                return Result.Success<T, ErrorResponse>(result?.Data ?? default(T));
            }
        }
    }
}
