using System;
using System.Text;
using Verse.DecoderDescriptors;
using Verse.Schemas.QueryString;

namespace Verse.Schemas
{
	public sealed class QueryStringSchema<TEntity> : ISchema<TEntity>
	{
		public IDecoderDescriptor<TEntity> DecoderDescriptor => this.decoderDescriptor;

	    public IEncoderDescriptor<TEntity> EncoderDescriptor => throw new NotImplementedException("encoding not implemented");

	    private readonly DecoderConverter decoderConverter;

		private readonly FlatDecoderDescriptor<TEntity, ReaderState, string> decoderDescriptor;

		public QueryStringSchema(Encoding encoding)
		{
			var sourceConverter = new DecoderConverter();

			this.decoderConverter = sourceConverter;
			this.decoderDescriptor = new FlatDecoderDescriptor<TEntity, ReaderState, string>(sourceConverter, new ReaderSession(encoding), new Reader<TEntity>());
		}

		public QueryStringSchema() :
			this(new UTF8Encoding(false))
		{
		}

		/// <inheritdoc/>
		public IDecoder<TEntity> CreateDecoder()
		{
			return this.decoderDescriptor.CreateDecoder();
		}

		/// <inheritdoc/>
		public IEncoder<TEntity> CreateEncoder()
		{
			throw new NotImplementedException("encoding not implemented");
		}

		public void SetDecoderConverter<U>(Converter<string, U> converter)
		{
			this.decoderConverter.Set(converter);
		}
	}
}
