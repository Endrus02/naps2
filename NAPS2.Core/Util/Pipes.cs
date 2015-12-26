﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;

namespace NAPS2.Util
{
    /// <summary>
    /// Simple inter-process communication between NAPS2 instances via named pipes.
    /// </summary>
    public static class Pipes
    {
        public const string MSG_SCAN_WITH_DEVICE = "SCAN_WDEV_";
        public const string MSG_KILL_PIPE_SERVER = "KILL_PIPE_SERVER";
        // TODO: Kill won't work properly when multiple servers are running
        // TODO: See http://stackoverflow.com/a/10485210/2112909 for a possible fix

        // An arbitrary non-secret unique name.
        // This could be edtion/version-specific, but I like the idea that if the user is running a portable version and
        // happens to have NAPS2 installed too, the scan button will propagate to the portable version.
        private const string PIPE_NAME = "NAPS2_PIPE_86a6ef67-742a-44ec-9ca5-64c5bddfd013";
        // The timeout is small since pipe connections should be on the local machine only.
        private const int TIMEOUT = 1000;

        private static bool _serverRunning;

        /// <summary>
        /// Send a message to a NAPS2 instance running a pipe server. If multiple instances are running servers then the recipient is essentially random.
        /// </summary>
        /// <param name="msg">The message to send.</param>
        public static bool SendMessage(string msg)
        {
            try
            {
                using (var pipeClient = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.Out))
                {
                    //MessageBox.Show("Sending msg:" + msg);
                    pipeClient.Connect(TIMEOUT);
                    var streamString = new StreamString(pipeClient);
                    streamString.WriteString(msg);
                    //MessageBox.Show("Sent");
                    return true;
                }
            }
            catch (Exception e)
            {
                Log.ErrorException("Error sending message through pipe", e);
                return false;
            }
        }

        /// <summary>
        /// Start a pipe server on a background thread, calling the callback each time a message is received. Only one pipe server can be running per process.
        /// </summary>
        /// <param name="msgCallback">The message callback.</param>
        public static void StartServer(Action<string> msgCallback)
        {
            if (_serverRunning)
            {
                return;
            }
            var thread = new Thread(() =>
            {
                try
                {
                    using (var pipeServer = new NamedPipeServerStream(PIPE_NAME, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances))
                    {
                        while (true)
                        {
                            pipeServer.WaitForConnection();
                            var streamString = new StreamString(pipeServer);
                            var msg = streamString.ReadString();
                            //MessageBox.Show("Received msg:" + msg);
                            if (msg == MSG_KILL_PIPE_SERVER)
                            {
                                break;
                            }
                            msgCallback(msg);
                            pipeServer.Disconnect();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.ErrorException("Error in named pipe server", ex);
                }
                _serverRunning = false;
            });
            _serverRunning = true;
            thread.Start();
        }

        /// <summary>
        /// Kills the pipe server background thread if one is running.
        /// </summary>
        public static void KillServer()
        {
            if (_serverRunning)
            {
                SendMessage(MSG_KILL_PIPE_SERVER);
            }
        }

        /// <summary>
        /// From https://msdn.microsoft.com/en-us/library/bb546085%28v=vs.110%29.aspx
        /// </summary>
        private class StreamString
        {
            private Stream ioStream;
            private UnicodeEncoding streamEncoding;

            public StreamString(Stream ioStream)
            {
                this.ioStream = ioStream;
                streamEncoding = new UnicodeEncoding();
            }

            public string ReadString()
            {
                int len;
                len = ioStream.ReadByte() * 256;
                len += ioStream.ReadByte();
                byte[] inBuffer = new byte[len];
                ioStream.Read(inBuffer, 0, len);

                return streamEncoding.GetString(inBuffer);
            }

            public int WriteString(string outString)
            {
                byte[] outBuffer = streamEncoding.GetBytes(outString);
                int len = outBuffer.Length;
                if (len > UInt16.MaxValue)
                {
                    len = (int)UInt16.MaxValue;
                }
                ioStream.WriteByte((byte)(len / 256));
                ioStream.WriteByte((byte)(len & 255));
                ioStream.Write(outBuffer, 0, len);
                ioStream.Flush();

                return outBuffer.Length + 2;
            }
        }
    }
}