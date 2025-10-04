using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleService
{
    class Program
    {
        static void Main(string[] args)
        {
            ChromeleonService chromeleonService = new ChromeleonService();
            chromeleonService.ProcessBackService();
        }
    }
}
