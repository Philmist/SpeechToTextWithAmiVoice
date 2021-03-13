#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net;
using System.Diagnostics;

namespace SpeechToTextWithAmiVoice
{
    class TextHttpSender
    {
        public class RecognizedText
        {
            public string code { get; set; } = "R";
            public string text { get; set; } = "";
        }

        public string Uri { get; protected set; } = "";
        public bool isEnable { get; protected set; } = true;

        public TextHttpSender(string Uri) {
            this.Uri = Uri;
            if (String.IsNullOrWhiteSpace(this.Uri))
            {
                isEnable = false;
            }
        }

        public bool Send(RecognizedText text)
        {
            if (!isEnable)
            {
                return false;
            }

            var options = new JsonSerializerOptions { WriteIndented = true, };
            var jsonUtf8String = JsonSerializer.SerializeToUtf8Bytes(text, options);

            WebRequest request = WebRequest.Create(Uri);
            request.Method = "POST";
            request.ContentLength = jsonUtf8String.Length;
            request.ContentType = "application/json";
            request.Timeout = 10000;

            var dataStream = request.GetRequestStream();
            var jsonString = JsonSerializer.Serialize(text, options);
            Debug.WriteLine(jsonString);
            dataStream.Write(jsonUtf8String, 0, jsonUtf8String.Length);
            dataStream.Close();

            try
            {
                var response = request.GetResponse();
                var statusCode = ((HttpWebResponse)response).StatusCode;
                if (statusCode == HttpStatusCode.OK)
                {
                    isEnable = true;
                    return true;
                }
            }
            catch (WebException ex)
            {
                Debug.WriteLine(String.Format("HTTP Exception: {0}", ex.Message));
                isEnable = false;
                return false;
            }

            isEnable = false;
            return false;
        }
    }
}
