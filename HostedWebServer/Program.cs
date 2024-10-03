using Microsoft.Owin.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HostedWebServer
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string baseAddress = "http://localhost:8080/";

            using (WebApp.Start<Startup>(url: baseAddress))
            {
                Console.WriteLine("Server started. Listening on " + baseAddress);
                Console.WriteLine("Press any key to stop the server...");
                Console.ReadKey();
            }
        }
    }
}
