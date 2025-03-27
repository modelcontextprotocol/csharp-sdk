//using ModelContextProtocol.Server;
//using System;
//using System.Collections.Generic;
//using System.ComponentModel;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

//namespace EverythingServer.Tools;

//[McpServerToolType]
//public static class LongRunningTool
//{
//    [McpServerTool, Description("Demonstrates a long running operation with progress updates")]
//    public static async Task<string> LongRunningOperation(int duration, int steps)
//    {
//        var stepDuration = duration / steps;

//        for (int i = 1; i <= steps + 1; i++)
//        {
//            // Simulate a long-running operation
//            await Task.Delay(stepDuration);
//            // Report progress
//            var progress = (i * 100) / steps;
//            Console.WriteLine($"Progress: {progress}%");
//        }
//    }
//}
