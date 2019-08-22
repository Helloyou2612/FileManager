using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FileManager.Common.Helper
{
    public static class FileHelper
    {
        public static byte[] GetFileAsBytes(string filePath)
        {
            return File.ReadAllBytes(filePath);
        }

        public static byte[] GetStreamAsBytes(Stream input)
        {
            using (var memoryStream = new MemoryStream())
            {
                input.CopyTo(memoryStream);
                return memoryStream.ToArray();
            }
        }

        public static Stream GetBytesAsStream(byte[] input)
        {
            return new MemoryStream(input, false);
        }

        //bỏ dấu tiếng việt
        public static string convertToUnSign(string s)
        {
            Regex regex = new Regex("\\p{IsCombiningDiacriticalMarks}+");
            string temp = s.Normalize(NormalizationForm.FormD);
            return regex.Replace(temp, String.Empty).Replace('\u0111', 'd').Replace('\u0110', 'D');
        }
    }
}