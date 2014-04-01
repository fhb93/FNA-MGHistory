#region License
/* FNA - XNA4 Reimplementation for Desktop Platforms
 * Copyright 2009-2014 Ethan Lee and the MonoGame Team
 *
 * Released under the Microsoft Public License.
 * See LICENSE for details.
 */
#endregion

#region Using Statements
using System;
using System.IO;

using OpenTK.Audio.OpenAL;
#endregion

namespace Microsoft.Xna.Framework.Audio
{
	// http://msdn.microsoft.com/en-us/library/microsoft.xna.framework.audio.soundeffect.aspx
	public sealed class SoundEffect : IDisposable
	{
		#region Public Properties

		public TimeSpan Duration
		{
			get;
			private set;
		}

		public bool IsDisposed
		{
			get;
			private set;
		}

		public string Name
		{
			get;
			set;
		}

		#endregion

		#region Public Static Properties

		// FIXME: This should affect all sounds! alListener? -flibit
		private static float INTERNAL_masterVolume = 1.0f;
		public static float MasterVolume
		{
			get
			{
				return INTERNAL_masterVolume;
			}
			set
			{
				INTERNAL_masterVolume = value;
			}
		}

		// FIXME: How does this affect OpenAL? -flibit
		private static float INTERNAL_distanceScale = 1.0f;
		public static float DistanceScale
		{
			get
			{
				return INTERNAL_distanceScale;
			}
			set
			{
				if (value <= 0.0f)
				{
					throw new ArgumentOutOfRangeException("value of DistanceScale");
				}
				INTERNAL_distanceScale = value;
			}
		}

		// FIXME: How does this affect OpenAL? -flibit
		private static float INTERNAL_dopplerScale = 1.0f;
		public static float DopplerScale
		{
			get
			{
				return INTERNAL_dopplerScale;
			}
			set
			{
				if (value <= 0.0f)
				{
					throw new ArgumentOutOfRangeException("value of DopplerScale");
				}
				INTERNAL_dopplerScale = value;
			}
		}

		// FIXME: How does this affect OpenAL? -flibit
		private static float INTERNAL_speedOfSound = 343.5f;
		public static float SpeedOfSound
		{
			get
			{
				return INTERNAL_speedOfSound;
			}
			set
			{
				INTERNAL_speedOfSound = value;
			}
		}

		#endregion

		#region Internal Audio Data

		internal int INTERNAL_buffer;

		#endregion

		#region Public Constructors

		public SoundEffect(
			byte[] buffer,
			int sampleRate,
			AudioChannels channels
		) {
			INTERNAL_bufferData(
				buffer,
				(uint) sampleRate,
				(uint) channels,
				0,
				0,
				0
			);
		}

		public SoundEffect(
			byte[] buffer,
			int offset,
			int count,
			int sampleRate,
			AudioChannels channels,
			int loopStart,
			int loopLength
		) {
			INTERNAL_bufferData(
				buffer,
				(uint) sampleRate,
				(uint) channels,
				loopStart,
				loopStart + loopLength,
				0
			);
		}

		#endregion

		#region Internal Constructors

		internal SoundEffect(string fileName)
		{
			if (fileName == string.Empty)
			{
				throw new ArgumentNullException("fileName");
			}

			Name = Path.GetFileNameWithoutExtension(fileName);

			Stream s;
			try
			{
				s = File.OpenRead(fileName);
			}
			catch (IOException e)
			{
				throw new Content.ContentLoadException("Could not load audio data", e);
			}

			INTERNAL_loadAudioStream(s);
			s.Close();
		}

		internal SoundEffect(Stream s)
		{
			INTERNAL_loadAudioStream(s);
		}

		internal SoundEffect(
			string name,
			byte[] buffer,
			uint sampleRate,
			uint channels,
			uint loopStart,
			uint loopLength,
			uint compressionAlign
		) {
			Name = name;
			INTERNAL_bufferData(
				buffer,
				sampleRate,
				channels,
				loopStart,
				loopStart + loopLength,
				compressionAlign
			);
		}

		#endregion

		#region Public Dispose Method

		public void Dispose()
		{
			if (!IsDisposed)
			{
				AL.DeleteBuffer(INTERNAL_buffer);
				IsDisposed = true;
			}
		}

		#endregion

		#region Additional SoundEffect/SoundEffectInstance Creation Methods

		public SoundEffectInstance CreateInstance()
		{
			return new SoundEffectInstance(this);
		}

		public static SoundEffect FromStream(Stream stream)
		{
			return new SoundEffect(stream);
		}

		#endregion

		#region Public Play Methods

		public bool Play()
		{
			// FIXME: Perhaps MasterVolume should be applied to alListener? -flibit
			return Play(MasterVolume, 0.0f, 0.0f);
		}

		public bool Play(float volume, float pitch, float pan)
		{
			SoundEffectInstance instance = CreateInstance();
			instance.Volume = volume;
			instance.Pitch = pitch;
			instance.Pan = pan;
			instance.Play();
			if (instance.State != SoundState.Playing)
			{
				// Ran out of AL sources, probably.
				instance.Dispose();
				return false;
			}
			OpenALDevice.Instance.instancePool.Add(instance);
			return true;
		}

		#endregion

		#region Private OpenAL Loading Methods

		private void INTERNAL_loadAudioStream(Stream s)
		{
			byte[] data;
			uint sampleRate = 0;
			uint numChannels = 0;

			using (BinaryReader reader = new BinaryReader(s))
			{
				// RIFF Signature
				string signature = new string(reader.ReadChars(4));
				if (signature != "RIFF")
				{
					throw new NotSupportedException("Specified stream is not a wave file.");
				}

				reader.ReadUInt32(); // Riff Chunk Size

				string wformat = new string(reader.ReadChars(4));
				if (wformat != "WAVE")
				{
					throw new NotSupportedException("Specified stream is not a wave file.");
				}

				// WAVE Header
				string format_signature = new string(reader.ReadChars(4));
				while (format_signature != "fmt ")
				{
					reader.ReadBytes(reader.ReadInt32());
					format_signature = new string(reader.ReadChars(4));
				}

				int format_chunk_size = reader.ReadInt32();

				// Header Information
				uint audio_format = reader.ReadUInt16();	// 2
				numChannels = reader.ReadUInt16();		// 4
				sampleRate = reader.ReadUInt32();		// 8
				reader.ReadUInt32();				// 12, Byte Rate
				reader.ReadUInt16();				// 14, Block Align
				reader.ReadUInt16();				// 16, Bits Per Sample

				if (audio_format != 1)
				{
					throw new NotSupportedException("Wave compression is not supported.");
				}

				// Reads residual bytes
				if (format_chunk_size > 16)
				{
					reader.ReadBytes(format_chunk_size - 16);
				}

				// data Signature
				string data_signature = new string(reader.ReadChars(4));
				while (data_signature.ToLower() != "data")
				{
					reader.ReadBytes(reader.ReadInt32());
					data_signature = new string(reader.ReadChars(4));
				}
				if (data_signature != "data")
				{
					throw new NotSupportedException("Specified wave file is not supported.");
				}

				int waveDataLength = reader.ReadInt32();
				data = reader.ReadBytes(waveDataLength);
			}

			INTERNAL_bufferData(
				data,
				sampleRate,
				numChannels,
				0,
				0,
				0
			);
		}

		private void INTERNAL_bufferData(
			byte[] data,
			uint sampleRate,
			uint channels,
			uint loopStart,
			uint loopEnd,
			uint compressionAlign
		) {
			// FIXME: MSADPCM Duration
			Duration = TimeSpan.FromSeconds(data.Length / 2 / channels / ((double) sampleRate));

			// Generate the buffer now, in case we need to perform alBuffer ops.
			INTERNAL_buffer = AL.GenBuffer();

			ALFormat format;
			if (compressionAlign > 0)
			{
				format = (channels == 2) ? ALFormat.StereoMsadpcmSoft : ALFormat.MonoMsadpcmSoft;
				AL.Buffer(INTERNAL_buffer, ALBufferi.UnpackBlockAlignmentSoft, compressionAlign);
			}
			else
			{
				format = (channels == 2) ? ALFormat.Stereo16 : ALFormat.Mono16;
			}

			// Load it!
			AL.BufferData(
				INTERNAL_buffer,
				format,
				data,
				data.Length,
				(int) sampleRate
			);

			// Set the loop points, if applicable
			if (loopStart > 0 || loopEnd > 0)
			{
				AL.Buffer(
					INTERNAL_buffer,
					ALBufferiv.LoopPointsSoft,
					new uint[]
					{
						loopStart,
						loopEnd
					}
				);
			}
		}

		#endregion
	}
}
