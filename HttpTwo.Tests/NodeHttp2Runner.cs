using System;
using System.Diagnostics;
using System.IO;

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
            
            var scriptPath = Path.Combine (AppDomain.CurrentDomain.BaseDirectory, "..", "..", "node-http2", "example", "server.js");

            process = new Process ();
            process.StartInfo = new ProcessStartInfo ("/usr/local/bin/node", scriptPath);
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

            process.Start ();
            process.BeginOutputReadLine ();
            process.BeginErrorReadLine ();
        }

        public void StopServer ()
        {
            if (process != null && process.HasExited) { 
                process.Kill ();
                process.Close ();
                process = null;
            }
        }
    }
}

