﻿using System;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using LiveSplit.ComponentUtil;
using System.IO;
using System.Drawing;

namespace StarDisplay
{
    public class MemoryManager
    {
        public readonly Process Process;
        public LayoutDescription ld;
        GraphicsManager gm;
        public ROMManager rm;

        int previousTime;
        byte[] oldStars;
        public byte[] highlightPivot { get; private set; }

        byte[] defPicture;

        DeepPointer igt;
        public readonly DeepPointer[] files;

        DeepPointer romNamePtr;
        DeepPointer absoluteRomPathPtr;

        DeepPointer levelPtr;
        DeepPointer starPtr;
        DeepPointer redsPtr;

        DeepPointer segmentsTablePtr;

        private int[] courseLevels = { 0, 9, 24, 12, 5, 4, 7, 22, 8, 23, 10, 11, 36, 13, 14, 15 };
        private int[] secretLevels = { 0, 17, 19, 21, 27, 28, 29, 18, 31, 20, 25 };
        private int[] overworldLevels = { 6, 26, 16 };

        public int selectedFile;

        public MemoryManager(Process process, LayoutDescription ld, GraphicsManager gm, ROMManager rm, byte[] highlightPivot)
        {
            this.Process = process;
            this.ld = ld;
            this.gm = gm;
            this.rm = rm;
            this.highlightPivot = highlightPivot;
            oldStars = new byte[32];

            if (process != null)
            {
                //string version = process.MainModule.FileVersionInfo.FileVersion;

                //if (version == null || version.Contains("1.6"))
                //{
                    igt = new DeepPointer("Project64.exe", 0xD6A1C, 0x32D580);
                    files = new DeepPointer[4];
                    files[0] = new DeepPointer("Project64.exe", 0xD6A1C, 0x207708);
                    files[1] = new DeepPointer("Project64.exe", 0xD6A1C, 0x207778);
                    files[2] = new DeepPointer("Project64.exe", 0xD6A1C, 0x2077E8);
                    files[3] = new DeepPointer("Project64.exe", 0xD6A1C, 0x207858);

                    romNamePtr = new DeepPointer("Project64.exe", 0xAF1F8);
                    absoluteRomPathPtr = new DeepPointer("Project64.exe", 0xAF0F0);

                    levelPtr = new DeepPointer("Project64.exe", 0xD6A1C, 0x32DDFA);
                    starPtr = new DeepPointer("Project64.exe", 0xD6A1C, 0x064F80 + 0x04800);
                    redsPtr = new DeepPointer("Project64.exe", 0xD6A1C, 0x3613FD);

                    segmentsTablePtr = new DeepPointer("Project64.exe", 0xD6A1C, 0x33B400);
                /*}
                else if (version.Contains("2.3"))
                {
                    int baseOffset = 0x4B120000 - process.MainModule.BaseAddress.ToInt32();

                    igt = new DeepPointer("Project64.exe", baseOffset + 0x32D580);
                    files = new DeepPointer[4];
                    files[0] = new DeepPointer("Project64.exe", baseOffset + 0x207708);
                    files[1] = new DeepPointer("Project64.exe", baseOffset + 0x207778);
                    files[2] = new DeepPointer("Project64.exe", baseOffset + 0x2077E8);
                    files[3] = new DeepPointer("Project64.exe", baseOffset + 0x207858);

                    romNamePtr = null; //new DeepPointer("Project64.exe", 0xAF1F8); //rip these options
                    absoluteRomPathPtr = null; //new DeepPointer("Project64.exe", 0xAF0F0);

                    levelPtr = new DeepPointer("Project64.exe", baseOffset + 0x32DDFA);
                    starPtr = new DeepPointer("Project64.exe", baseOffset + 0x064F80 + 0x04800);
                    redsPtr = new DeepPointer("Project64.exe", baseOffset + 0x3613FD);

                    segmentsTablePtr = new DeepPointer("Project64.exe", baseOffset + 0x33B400);
                }*/
            }

            defPicture = File.ReadAllBytes("images/star.rgba16");
        }

        public bool ProcessActive()
        {
            return Process == null || Process.HasExited;
        }

        public void DeleteStars()
        {
            int curTime = igt.Deref<int>(Process);
            if (curTime > 200 || curTime < 60) return;

            previousTime = curTime;
            byte[] data = Enumerable.Repeat((byte)0x00, 0x70).ToArray();
            IntPtr ptr;

            DeepPointer file = files[selectedFile];
            if (!file.DerefOffsets(Process, out ptr))
            {
                Console.WriteLine("deref fail");
            }
            if (!Process.WriteBytes(ptr, data))
            {
                throw new IOException();
            }
        }

        public string GetROMName()
        {
            if (romNamePtr == null) return "";
            return romNamePtr.DerefString(Process, 32);
        }

        public string GetAbsoluteROMPath()
        {
            if (absoluteRomPathPtr == null) return "";
            return absoluteRomPathPtr.DerefString(Process, 255);
        }

        private int GetCurrentOffset()
        {
            int level = levelPtr.Deref<byte>(Process);
            if (level == 0) return -1;
            int courseLevel = Array.FindIndex(courseLevels, lvl => lvl == level);
            if (courseLevel != -1) return courseLevel + 3;
            int secretLevel = Array.FindIndex(secretLevels, lvl => lvl == level);
            if (secretLevel != -1) return secretLevel + 18;
            int owLevel = Array.FindIndex(overworldLevels, lvl => lvl == level);
            if (owLevel != -1) return 0;
            return -2;
        }

        public TextHighlightAction GetCurrentLineAction()
        {
            int offset = GetCurrentOffset();

            int courseIndex = Array.FindIndex(ld.courseDescription, lind => lind != null && !lind.isTextOnly && lind.offset == offset);
            if (courseIndex != -1) return new TextHighlightAction(courseIndex, false, ld.courseDescription[courseIndex].text);
            int secretIndex = Array.FindIndex(ld.secretDescription, lind => lind != null && !lind.isTextOnly && lind.offset == offset);
            if (secretIndex != -1) return new TextHighlightAction(secretIndex, true, ld.secretDescription[secretIndex].text);

            return null;
        }

        static public int countStars(byte stars)
        {
            int answer = 0;
            for (int i = 1; i <= 7; i++)
                answer += ((stars & (1 << (i - 1))) == 0) ? 0 : 1;
            return answer;
        }

        public sbyte GetReds()
        {
            return redsPtr.Deref<sbyte>(Process);
        }

        public Bitmap GetImage()
        {
            byte[] data = starPtr.DerefBytes(Process, 512);

            for (int i = 0; i < 512; i += 4) //TODO: Better ending convert
            {
                byte[] copy = new byte[4];
                copy[0] = data[i + 0];
                copy[1] = data[i + 1];
                copy[2] = data[i + 2];
                copy[3] = data[i + 3];
                data[i + 0] = copy[3];
                data[i + 1] = copy[2];
                data[i + 2] = copy[1];
                data[i + 3] = copy[0];
            }
            return FromRGBA16(data);
        }

        public Bitmap FromRGBA16(byte[] data)
        {
            Bitmap picture = new Bitmap(16, 16);
            for (int i = 0; i < 16; i++)
            {
                for (int j = 0; j < 16; j++)
                {
                    int offset = (16 * j + i) * 2;
                    int colorARGB = (data[offset + 1] & 0x01) * 255 << 24
                        | (data[offset] & 0xF8) << 16 | (data[offset] & 0xE0) << 11
                        | (data[offset] & 0x07) << 13 | (data[offset] & 0x07) << 8
                        | (data[offset + 1] & 0xC0) << 5
                        | (data[offset + 1] & 0x3E) << 2 | (data[offset + 1] & 0x38) >> 3;

                    Color c = Color.FromArgb(colorARGB);
                    picture.SetPixel(i, j, c);
                }
            }
            return picture;
        }

        public void resetHighlightPivot()
        {
            highlightPivot = null;
        }

        public DrawActions GetDrawActions()
        {
            int length = 32;
            DeepPointer file = files[selectedFile];
            byte[] stars = file.DerefBytes(Process, length);

            for (int i = 0; i < length; i += 4) //TODO: Better ending convert
            {
                byte[] copy = new byte[4];
                copy[0] = stars[i + 0];
                copy[1] = stars[i + 1];
                copy[2] = stars[i + 2];
                copy[3] = stars[i + 3];
                stars[i + 0] = copy[3];
                stars[i + 1] = copy[2];
                stars[i + 2] = copy[1];
                stars[i + 3] = copy[0];
            }

            if (highlightPivot == null)
            {
                highlightPivot = stars;
            }

            int totalReds = 0;
            try
            {
                totalReds = rm != null ? rm.ParseReds(ld, GetCurrentLineAction()) : -1;
            }
            catch (IOException) { }

            int reds = GetReds();
            if (totalReds != 0)
                reds += totalReds - 8;

            if (totalReds == -1)
            {
                totalReds = 0; reds = 0;
            }
            
            DrawActions da = new DrawActions(ld, stars, oldStars, highlightPivot, reds, totalReds);
            oldStars = stars;
            return da;
        }

        public int SearchObjects(UInt32 searchBehaviour)
        {
            int count = 0;

            UInt32 address = 0x33D488;
            do
            {
                DeepPointer currentObject = new DeepPointer("Project64.exe", 0xD6A1C, (int)address);
                byte[] data = currentObject.DerefBytes(Process, 0x260);
                address = BitConverter.ToUInt32(data, 0x8) & 0x7FFFFFFF;

                UInt32 intparam = BitConverter.ToUInt32(data, 0x180);
                UInt32 behaviourActive1 = BitConverter.ToUInt32(data, 0x1CC);
                
                if (behaviourActive1 == searchBehaviour) count++;
            } while (address != 0x33D488);
            return count;
        }

        public void InvalidateCache()
        {
            oldStars = new byte[32];
        }
    }
}