using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace DynamicAgent
{
    internal class Program
    {
        static void Main(string[] args)
        {
            //Method method  = GenerateMethod.Generate(typeof(Program).GetMethod("CalculateMD5"), new object[] { @"C:\Users\Asif\Desktop\GokuSupreme.jpg", (char)0x2D, "" });
            
            //string s = (string)DynamicExecute.ExecuteMethod(method);
            //Console.WriteLine(s);

            // We need to supply the MethodInfo for the method we want to save, and the parameters we want to 
            // execute the method with - as an object array
            string methodJson  = GenerateMethod.GenerateMethodAsJSON(typeof(Program).GetMethod("CalculateMD5"), new object[] { @"C:\Users\Asif\Desktop\GokuSupreme.jpg", 0x2D, "" }, true);
            File.WriteAllText(@"C:\Users\Asif\Desktop\method.txt", methodJson);

            // Execute the method from the JSON string
            string s = (string)DynamicExecute.ExecuteMethod(methodJson);
            Console.WriteLine(s);

            Console.ReadKey();
        }

        public static string CalculateMD5(string filename, char charToRemove, string seperator)
        {
            // Just to demonstrate handling of a field
            Console.WriteLine(string.Empty);

            using (MD5 md5 = MD5.Create())
            {
                using (FileStream stream = File.OpenRead(filename))
                {
                    byte[] hash = md5.ComputeHash(stream);
                    
                    string hashStr = BitConverter.ToString(hash).ToLowerInvariant();
                    
                    // Just to demonstrate handling of a constructor
                    List<char> hashWithoutDash = new List<char>();

                    // Demonstrate handling of IEnumerable method
                    foreach (char c in hashStr.ToList())
                    {
                        if (c != charToRemove)
                        {
                            hashWithoutDash.Add(c);
                        }
                    }

                    // Demonstrate handling of generic method
                    return String.Join(seperator, hashWithoutDash);
                }
            }
        }
    }
}
