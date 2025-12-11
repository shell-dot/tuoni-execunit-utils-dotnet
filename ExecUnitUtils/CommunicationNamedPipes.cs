using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace ExecUnitUtils
{
    /// <summary>
    /// Manages client-side communication over named pipes using TLV-encoded messages.
    /// Supports connection, sending/receiving data, response handling with sequence numbers, and callbacks.
    /// </summary>
    public class CommunicationNamedPipes : IDisposable
    {
        protected const int DefaultConnectTimeoutMs = 5000; // 5 seconds

        protected volatile bool _active;
        protected NamedPipeClientStream _client;
        protected BinaryReader _reader;
        protected BinaryWriter _writer;
        protected Thread _listenThread;
        protected readonly object _sendLock;
        protected CancellationTokenSource _cts;

        /// <summary>
        /// Initializes a new instance with the pipe name
        /// </summary>
        public CommunicationNamedPipes(string name)
        {
            _client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            _active = false;
            _sendLock = new object();
            _cts = new CancellationTokenSource();
        }

        /// <summary>
        /// Connects to the named pipe and returns initial data if successful.
        /// </summary>
        /// <param name="timeoutMs">Connection timeout in milliseconds.</param>
        /// <returns>Initial data or null on failure.</returns>
        public byte[] Connect(int timeoutMs = 10000)
        {
            try
            {
                _client.Connect(timeoutMs);
                _reader = new BinaryReader(_client);
                _writer = new BinaryWriter(_client);
                _active = true;

                byte[] data = GetData();
                if (data == null)
                {
                    _active = false;
                    return null;
                }

                TLV tlv = new TLV();
                if (!tlv.Load(data, 0))
                {
                    _active = false;
                    return null;
                }

                _listenThread = new Thread(ListenForMessages);
                _listenThread.IsBackground = true;
                _listenThread.Start();

                return tlv.Data;
            }
            catch (TimeoutException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
            finally
            {
                if (!_active)
                    Dispose();
            }
        }

        /// <summary>
        /// Closes the connection gracefully.
        /// </summary>
        public void Close()
        {
            Dispose();
        }

        protected virtual bool HandleIncomingData(TLV tlv)
        {
            return false;
        }

        protected void ListenForMessages()
        {
            while (!_cts.IsCancellationRequested && _active)
            {
                byte[] data = GetData();
                if (data == null)
                {
                    break;
                }

                TLV tlv = new TLV();
                if (!tlv.Load(data, 0))
                    continue;

                HandleIncomingData(tlv);
            }
        }

        protected byte[] GetData()
        {
            if (!_active) return null;
            try
            {
                int len = _reader.ReadInt32();
                return _reader.ReadBytes(len);
            }
            catch (EndOfStreamException)
            {
                _active = false;
                return null;
            }
            catch (IOException)
            {
                _active = false;
                return null;
            }
        }

        protected bool PutData(byte[] data)
        {
            lock (_sendLock)
            {
                if (!_active) return false;
                try
                {
                    _writer.Write(data.Length);
                    _writer.Write(data);
                    _writer.Flush();
                    return true;
                }
                catch (IOException)
                {
                    _active = false;
                    return false;
                }
            }
        }

        public void Dispose()
        {
            if (!_active) return;
            _active = false;
            _cts.Cancel();

            try
            {
                _client.WaitForPipeDrain();
            }
            catch {}

            if (_client != null)
                _client.Dispose();
            if (_reader != null)
                _reader.Dispose();
            if (_writer != null)
                _writer.Dispose();
            if (_listenThread != null)
                _listenThread.Join(2000); // Wait briefly for thread to exit

            _cts.Dispose();
        }
    }
}