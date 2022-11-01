using System;
using System.Diagnostics;
using System.IO;
using HttpTwo.Internal;

namespace HttpTwo.Tests
{
    public class NodeHttp2Runner
    {
        Process process;

        public Action<string> LogHandler { get; set; }

        public void StartServer ()
        {
            if (process != null && !process.HasExited)
                return;

            // HTTP2_PLAIN=true HTTP2_LOG=trace HTTP2_LOG_DATA=1 node ./example/server.js
            var scriptPath = Path.Combine (AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "node-http2", "example", "server.js");

            process = new Process ();
            process.StartInfo = new ProcessStartInfo ("node", "\"" + scriptPath + "\"");
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.EnvironmentVariables.Add ("HTTP2_PLAIN", "true");
            process.StartInfo.EnvironmentVariables.Add ("HTTP2_LOG", "trace");
            process.StartInfo.EnvironmentVariables.Add ("HTTP2_LOG_DATA", "1");

            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;

            process.EnableRaisingEvents = true;


            process.ErrorDataReceived += (sender, e) => {
                if (LogHandler != null)
                    LogHandler (e.Data);
            };
            process.OutputDataReceived += (sender, e) => {
                if (LogHandler != null)
                    LogHandler (e.Data);
            };

            Log.Info ("Running: {0} {1}", process.StartInfo.FileName, process.StartInfo.Arguments);

            process.Start ();
            process.BeginOutputReadLine ();
            process.BeginErrorReadLine ();
        }

        public void StopServer ()
        {
            if (process != null && !process.HasExited) { 
                process.Kill ();
                process.Close ();
                process = null;
            }
        }
    }
}

