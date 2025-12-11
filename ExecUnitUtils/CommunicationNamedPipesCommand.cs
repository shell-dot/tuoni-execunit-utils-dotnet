using System;
using System.Collections.Generic;
using System.Text;

namespace ExecUnitUtils
{
    internal class CommunicationNamedPipesCommand : CommunicationNamedPipes
    {
        protected const byte MessageTypeResult = 0x30;
        protected const byte MessageTypeConf = 0x31;
        protected const byte MessageTypeConf_ongoing = 0x1;
        protected const byte MessageTypeConf_stoptime = 0x3;
        protected const byte MessageTypeError = 0x32;
        protected const byte MessageTypeSuccess = 0x33;
        protected const byte MessageTypeFailed = 0x34;
        protected const byte MessageTypeStop = 0x3F;
        protected const byte MessageTypeNewData = 0x39;

        protected Action<byte[]> _actionNewData = null;
        protected Action _actionStop = null;

        /// <summary>
        /// Initializes a new instance with the pipe name and optional callbacks for new data and stop actions.
        /// </summary>
        /// <param name="pipeName">The name of the named pipe.</param>
        /// <param name="newData">The action to invoke when new data is received.</param>
        /// <param name="stop">The action to invoke when a stop message is received.</param>
        public CommunicationNamedPipesCommand(string pipeName, Action<byte[]> newData, Action stop) : base(pipeName)
        {
            _actionNewData = newData;
            _actionStop = stop;
        }

        /// <summary>
        /// Sends a result message with the provided data.
        /// </summary>
        /// <param name="data">The data to send as the result.</param>
        public void sendResult(byte[] data)
        {
            TLV tlv = new TLV(MessageTypeResult, data);
            PutData(tlv.GetFullBuffer());
        }

        /// <summary>
        /// Sends an error message with the provided message bytes.
        /// </summary>
        /// <param name="msg">The error message bytes to send.</param>
        public void sendError(byte[] msg)
        {
            TLV tlv = new TLV(MessageTypeError, msg);
            PutData(tlv.GetFullBuffer());
        }

        /// <summary>
        /// Sends a success return message.
        /// </summary>
        public void sendReturnSuccess()
        {
            TLV tlv = new TLV(MessageTypeSuccess, new byte[0]);
            PutData(tlv.GetFullBuffer());
        }

        /// <summary>
        /// Sends a failed return message.
        /// </summary>
        public void sendReturnFailed()
        {
            TLV tlv = new TLV(MessageTypeFailed, new byte[0]);
            PutData(tlv.GetFullBuffer());
        }

        /// <summary>
        /// Sends a configuration message indicating an ongoing result.
        /// </summary>
        public void sendConf_ongoingResult()
        {
            TLV tlv = new TLV(MessageTypeConf);
            tlv.AddChild(new TLV(MessageTypeConf_ongoing, new byte[1] { 0x1 }));
            PutData(tlv.GetFullBuffer());
        }


        /// <summary>
        /// Sends an configuration message indicating how long to wait before letting agent stop it by force after agent sent stop commang
        /// </summary>
        /// <param name="waitTime">Max time agent waits before stopping by force (milliseconds)</param>
        public void sendConf_stopWait(int waitTime)
        {
            TLV tlv = new TLV(MessageTypeConf);
            tlv.AddChild(new TLV(MessageTypeConf_stoptime, BitConverter.GetBytes(waitTime)));
            PutData(tlv.GetFullBuffer());
        }

        override protected bool HandleIncomingData(TLV tlv)
        {
            if (tlv.Type == MessageTypeStop)
            {
                if (_actionStop != null)
                    _actionStop();
                return true;
            }
            if (tlv.Type == MessageTypeNewData)
            {
                if (_actionNewData != null)
                    _actionNewData(tlv.Data);
                return true;
            }
            return false;
        }
    }
}