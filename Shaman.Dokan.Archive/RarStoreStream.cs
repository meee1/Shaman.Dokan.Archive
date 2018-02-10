using System;
using System.IO;
using SharpCompress.Archives.Rar;

namespace Shaman.Dokan
{
    public class RarStoreStream : Stream
    {
        private RarArchiveEntry entry;

        public RarStoreStream(RarArchiveEntry entry)
        {
            this.entry = entry;
        }

        public override void Flush()
        {
            
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Begin) Position = offset;
            else if (origin == SeekOrigin.Current) Position += offset;
            else if (origin == SeekOrigin.End) Position = Length + offset;
            else throw new ArgumentException();
            return Position;
        }

        public override void SetLength(long value)
        {
            throw new System.NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            long wantstart = Position;
            long wantend = Position + count;

            long fileoffset = 0;
            int read = 0;
            int part = 0;
            bool started = false;

            foreach (var entryRarPart in entry.RarParts)
            {
                var partstart = fileoffset;
                var partend = fileoffset + entryRarPart.FileHeader.CompressedSize;
                var partsize = entryRarPart.FileHeader.CompressedSize;
                
                if (wantstart >= partstart && wantstart < partend || started)
                {
                    started = true;
                    using (var st = entryRarPart.GetCompressedStream())
                    {
                        var offsetpartstart = (Position - partstart);
                        partsize -= offsetpartstart;
                        st.Position = entryRarPart.FileHeader.DataStartPosition + offsetpartstart;
                        Console.WriteLine("filepart {4} {0}/{1} {2} {3}",Position,Length, Math.Min(count, partsize), read , part);
                        read += st.Read(buffer, read + offset, (int) Math.Min(count - read, partsize));
                        Position += read;

                        if (Position == Length)
                            return read;

                        if (read != count)
                        {
                            // need to switch to next file part
                        }
                    }
                }

                if (read == count)
                    return read;

                fileoffset += entryRarPart.FileHeader.CompressedSize;
                part++;
            }

            return read;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new System.NotImplementedException();
        }

        public override bool CanRead { get; } = true;
        public override bool CanSeek { get; } = true;
        public override bool CanWrite { get; } = false;
        public override long Length
        {
            get { return entry.Size; }
        }
        public override long Position { get; set; }
    }
}