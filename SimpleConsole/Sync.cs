﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Speech.Synthesis;
using System.Threading;
using System.Speech.AudioFormat;
using System.IO;
using NAudio.Wave;
using System.Linq;

namespace SimpleConsole
{
    internal class SyncPool
    {
        private static List<Sync> _syncList;

        public static void Init()
        {
            if (_syncList != null) return;

            _readyStateSyncQueue = new Queue<Sync>();
            _syncList = new List<Sync> { new Sync(Queue) { pan = -1 }, new Sync(Queue) { pan = 1 } };
            _readyThread = new Thread(SpeechThread);
            _readyThread.Start();
        }

        private static int thread = 2;
        private static void SpeechThread()
        {
            while (true)
            {
                if (queue.Count > 0)
                {
                    Tuple<String, String> tup = null;
                    lock (queue)
                    {
                        if (thread > 0)
                        {
                            thread--;

                            tup = queue.Dequeue();
                            ThreadPool.QueueUserWorkItem((x) => { CreateThread(tup.Item1, tup.Item2); });
                        }
                    }
                }
            }
        }

        private static void CreateThread(string pUsername, string pText)
        {
            Sync synth = null;
            try
            {
                var split = pText.Split(' ').ToList().Distinct().ToArray();
                pText = String.Join(" ", split);

                while (true)
                {
                    if (_readyStateSyncQueue.Count > 0)
                    {
                        lock (_readyStateSyncQueue)
                        {
                            if (_readyStateSyncQueue.Count > 0)
                            {
                                synth = _readyStateSyncQueue.Dequeue();
                                break;
                            }
                        }
                    }

                    ResetEvent.WaitOne(1000);
                    ResetEvent.Reset();
                }

                synth.Synth.Rate = 2;
                synth.Synth.Speak(pUsername);

                synth.SetRate(pUsername, pText);
                synth.RandomVoice();

                // Speak a string.
                synth.Speak(pText);

            }
            catch
            {

            }
            finally
            {
                if (synth != null)
                    synth.Enqueue();

                thread++;
            }
        }


        private static int _syncIndex;
        private static readonly ManualResetEvent ResetEvent = new ManualResetEvent(false);
        private static Queue<Sync> _readyStateSyncQueue;
        private static Thread _readyThread;

        private static Queue<Tuple<String, String>> queue = new Queue<Tuple<string, string>>();

        public static void SpeakText(string pUsername, string pText)
        {
            lock (queue)
            {
                queue.Enqueue(new Tuple<string, string>(pUsername, pText));
            }
        }


        private static void Queue(Sync pSync, bool pAddOrRemove)
        {
            lock (_readyStateSyncQueue)
            {
                _readyStateSyncQueue.Enqueue(pSync);
            }
            ResetEvent.Set();
        }
    }

    internal delegate void SyncOp(Sync pSync, bool pBool);

    internal class Sync
    {
        public int pan = 0;
        static Sync()
        {

        }

        private readonly SyncOp _op;

        private System.IO.Stream AudioStream;
        public Sync(SyncOp pOp)
        {
            AudioStream = new MemoryStream();
            Synth = new SpeechSynthesizer();
            Synth.SetOutputToAudioStream(AudioStream, new SpeechAudioFormatInfo(44100, AudioBitsPerSample.Sixteen, AudioChannel.Stereo) { });
            _voices = Synth.GetInstalledVoices();

            _op = pOp;
            _op(this, true);
        }

        public void Enqueue()
        {
            _op(this, true);
        }

        public SpeechSynthesizer Synth;
        private readonly ReadOnlyCollection<InstalledVoice> _voices;
        private int _index;

        public void RandomVoice()
        {
            _index++;
            if (_index >= _voices.Count)
                _index = 0;

            try
            {
                var voice = _voices[_index];
                Synth.SelectVoice(voice.VoiceInfo.Name);
            }
            catch (Exception ex)
            {
                Logger.Log(ex.ToString());
            }
        }

        public void SetRate(string pUsername, string pMessage)
        {
            var n1 = (float)(pUsername.Length / Program.MaxLengthSoFar);
            var n2 = n1 * 2;
            var n3 = n2 - 1;
            var n4 = n3 * 5;

            var d = (int)n4;

            d = SpeedUp(d, pMessage);

            d = Math.Min(d, 10);
            d = Math.Max(-10, d);
            Synth.Rate = d;
        }

        public int SpeedUp(int v, string m)
        {
            int j = 0;
            if (m.Length > 50)
                j++;
            if (m.Length > 100)
                j++;
            if (m.Length > 200)
                j++;
            if (m.Length > 300)
                j++;

            for (int i = 0; i < j; i++)
            {
                v = v + (Math.Abs(v) / 2) + 2;
            }

            return v;
        }

        public void Speak(string pText)
        {
            Synth.Speak(pText);

            PlayAudio();

            if (AudioStream != null)
            {
                try { AudioStream.Dispose(); } catch { }
            }

            AudioStream = new MemoryStream();
            Synth.SetOutputToAudioStream(AudioStream, new SpeechAudioFormatInfo(44100, AudioBitsPerSample.Sixteen, AudioChannel.Stereo));
        }


        private DirectSoundOut audioOutput = new DirectSoundOut();
        public void PlayAudio()
        {
            AudioStream.Position = 0;
            using (WaveStream stream = new RawSourceWaveStream(AudioStream, new WaveFormat(44100, 16, 2)))
            using (WaveChannel32 wc = new WaveChannel32(stream, 1, pan) { PadWithZeroes = false })
            {
                audioOutput.Init(wc);

                audioOutput.Play();

                while (audioOutput.PlaybackState != PlaybackState.Stopped)
                {
                    Thread.Sleep(20);
                }

                audioOutput.Stop();
            }
        }


        public static void CopyStream(Stream input, Stream output, int bytes)
        {
            byte[] buffer = new byte[32768];
            int read;
            while (bytes > 0 &&
                   (read = input.Read(buffer, 0, Math.Min(buffer.Length, bytes))) > 0)
            {
                output.Write(buffer, 0, read);
                bytes -= read;
            }
        }
    }
}