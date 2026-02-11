using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace SpeechToText.Core
{
    public class RecognizedTextToFileWriter
    {
        public bool isEnable { get; protected set; }
        public string FilePath { get; protected set; }

        public RecognizedTextToFileWriter(string filePath)
        {
            isEnable = true;
            this.FilePath = filePath;
        }

        public bool Write(string message)
        {
            if (!isEnable)
            {
                return false;
            }

            if (String.IsNullOrWhiteSpace(FilePath))
            {
                isEnable = false;
                return false;
            }

            try
            {
                File.WriteAllText(FilePath, message, Encoding.UTF8);
            }
            catch (Exception)
            {
                isEnable = false;
                return false;
            }

            return true;
        }
    }
}
