#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net;
using System.Diagnostics;
using Microsoft.Extensions.Http;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace SpeechToText.Core
{
    public class TextHttpSender
    {
        public class RecognizedText
        {
            public string code = "R";
            public string text = "";
        }

        public string Uri { get; protected set; } = "";
        public bool IsEnable { get; protected set; } = true;
        private IHttpClientFactory _httpClientFactory;
        public TextHttpSender(string Uri, IHttpClientFactory httpClientFactory) {
            this.Uri = Uri;
            _httpClientFactory = httpClientFactory;
            if (String.IsNullOrWhiteSpace(this.Uri))
            {
                IsEnable = false;
            }
        }

        public async Task<bool> Send(RecognizedText text)
        {
            if (!IsEnable)
            {
                return false;
            }

            /*
            var options = new JsonSerializerOptions { WriteIndented = true, };
            var jsonUtf8String = JsonSerializer.SerializeToUtf8Bytes(text, options);
            */

            // WebRequest request = WebRequest.Create(Uri);
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            /*
            request.Method = "POST";
            request.ContentLength = jsonUtf8String.Length;
            request.ContentType = "application/json";
            request.Timeout = 10000;
            

            var dataStream = request.GetRequestStream();
            var jsonString = JsonSerializer.Serialize(text, options);
            Debug.WriteLine(jsonString);
            dataStream.Write(jsonUtf8String, 0, jsonUtf8String.Length);
            dataStream.Close();
            */

            try
            {
                /*
                var response = request.GetResponse();
                var statusCode = ((HttpWebResponse)response).StatusCode;
                if (statusCode == HttpStatusCode.OK)
                {
                    IsEnable = true;
                    return true;
                }
                */
                var response = await httpClient.PostAsJsonAsync(Uri, text);
                if (response.StatusCode == HttpStatusCode.OK) {
                    IsEnable = true;
                    return true;
                }
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine(String.Format("HTTP Exception: {0}", ex.Message));
                IsEnable = false;
                return false;
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"{ex.Message}");
                IsEnable = false;
                return false;
            }

            IsEnable = false;
            return false;
        }
    }
}
