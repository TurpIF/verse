using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Verse.Schemas.JSON
{
    internal class WriterContext
    {
        #region Properties

        public int Position
        {
            get
            {
                return this.position;
            }
        }

        #endregion

        #region Attributes / Instance

        private bool addComma;

        private string currentKey;

        private readonly bool ignoreNull;

        private int position;

        private readonly StreamWriter writer;

        #endregion

        #region Attributes / Static

        private static readonly char[][] ascii = new char[128][];

        private static readonly char[] hexa = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F' };

        #endregion

        #region Constructors

        public WriterContext(Stream stream, JSONSettings settings)
        {
            this.ignoreNull = settings.IgnoreNull;
            this.position = 0;
            this.currentKey = null;
            this.addComma = false;
            this.writer = new StreamWriter(stream, settings.Encoding);
        }

        static WriterContext()
        {
            for (int i = 0; i < 32; ++i)
                WriterContext.ascii[i] = new[] { '\\', 'u', '0', '0', WriterContext.hexa[(i >> 4) & 0xF], WriterContext.hexa[(i >> 0) & 0xF] };

            for (int i = 32; i < 128; ++i)
                WriterContext.ascii[i] = new[] { (char)i };

            WriterContext.ascii['\b'] = new[] { '\\', 'b' };
            WriterContext.ascii['\f'] = new[] { '\\', 'f' };
            WriterContext.ascii['\n'] = new[] { '\\', 'n' };
            WriterContext.ascii['\r'] = new[] { '\\', 'r' };
            WriterContext.ascii['\t'] = new[] { '\\', 't' };
            WriterContext.ascii['\\'] = new[] { '\\', '\\' };
            WriterContext.ascii['"'] = new[] { '\\', '\"' };
        }

        #endregion

        #region Methods

        public void ArrayBegin()
        {
            this.PrepareToInsertEntry();
            this.writer.Write('[');
            this.addComma = false;
        }

        public void ArrayEnd()
        {
            this.writer.Write(']');
            this.addComma = true;
        }

        public void Key(string key)
        {
            this.currentKey = key;
        }

        public void Flush()
        {
            this.writer.Flush();
        }

        public void ObjectBegin()
        {
            this.PrepareToInsertEntry();
            this.writer.Write('{');
            this.addComma = false;
        }

        public void ObjectEnd()
        {
            this.writer.Write('}');
            this.addComma = true;
        }

        public void String(string value)
        {
            this.writer.Write('"');

            foreach (char c in value)
            {
                if (c < 128)
                    this.writer.Write(WriterContext.ascii[(int)c]);
                else
                {
                    this.writer.Write('\\');
                    this.writer.Write('u');
                    this.writer.Write(WriterContext.hexa[(c >> 12) & 0xF]);
                    this.writer.Write(WriterContext.hexa[(c >> 8) & 0xF]);
                    this.writer.Write(WriterContext.hexa[(c >> 4) & 0xF]);
                    this.writer.Write(WriterContext.hexa[(c >> 0) & 0xF]);
                }
            }

            this.writer.Write('"');
        }

        public void Value(Value value)
        {
            switch (value.Type)
            {
                case Content.Boolean:
                    this.PrepareToInsertEntry();
                    this.writer.Write(value.Boolean ? "true" : "false");

                    break;

                case Content.DecimalNumber:
                    this.PrepareToInsertEntry();
                    this.writer.Write(value.DecimalNumber.ToString(CultureInfo.InvariantCulture));

                    break;

                case Content.String:
                    this.PrepareToInsertEntry();
                    this.String(value.String);

                    break;

                default:
                    if (this.ignoreNull)
                    {
                        this.currentKey = null;

                        return;
                    }

                    this.PrepareToInsertEntry();
                    this.writer.Write("null");

                    break;
            }

            this.addComma = true;
        }

        private void PrepareToInsertEntry()
        {
            if (this.addComma)
                this.writer.Write(',');

            if (this.currentKey != null)
            {
                this.String(this.currentKey);

                this.writer.Write(':');
                this.currentKey = null;
            }
        }

        #endregion
    }
}