﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CommandLine;
using CommandLine.Text;
using SonarLogAPI;
using SonarLogAPI.CSV;
using SonarLogAPI.Lowrance;
using SonarLogAPI.Primitives;

namespace ConsoleLogConverter.Core
{
    public class Program
    {
        private class Options
        {
            [Option('i', "input", Required = true,
                HelpText = "Input file name to be processed.")]
            public string InputFile { get; set; }

            [Option('s', "search", Required = false, Default = -1,
                HelpText =
                    "Enables research mode. Research value at specified byte offset inside frame will be printed to console.")]
            public int SearchOffset { get; set; }

            [Option('d', "dpajust", Required = false,
                HelpText = "Input filename for depth adjust")]
            public string DepthAdjustFile { get; set; }

            [Option('o', "output", Separator = ':',
                HelpText = "Enable conversion mode. Output file version. sl2:sl3:csv")]
            public IList<string> OutputFileVersion { get; set; }

            [Option('c', "channel", Separator = ':', Required = false,
                HelpText =
                    "Channels, included in output file. Format: channel numbers, separated by colon. By default: all channels. Primary = 0, Secondary = 1, DownScan = 2, SidescanLeft = 3, SidescanRight = 4, SidescanComposite = 5, ThreeD = 9")]
            public IList<string> Channels { get; set; }

            [Option('f', "from", Default = 0,
                HelpText = "Get frames From specifieg number")]
            public int FramesFrom { get; set; }

            [Option('t', "to", Default = int.MaxValue,
                HelpText = "Get frames To specifieg number")]
            public int FramesTo { get; set; }

            [Option('a', "anonymous", Required = false, Default = false,
                HelpText = "Makes output file GPS coordinates anonymous. Sets Latitude and Longitude to zero.")]
            public bool CoordinatesDelete { get; set; }

            [Option('l', "flip", Required = false, Default = false,
                HelpText = "Flip SoundedData for SidescanComposite channel.")]
            public bool FlipSoundedData { get; set; }

            [Option('v', "verbose", Default = true,
                HelpText = "Prints all messages to standard output.")]
            public bool Verbose { get; set; }

            [Option('h', "dshift", Required = false,
                HelpText = "Depth value for adding or subtraction to the depth values in the log.")]
            public string DepthShift { get; set; }

            [Option('g', "generate", Separator = ':', Required = false,
                HelpText =
                    "Generate frames for specific channel from the frames at other channel(s). Format: numbers and chars, " +
                    "separated by colon. " +
                    "Options: numbers - destination frame channel and source channel(s); " +
                    "d - if specified generate sounded data from source frame depth value,(otherwise take sounded " +
                    "data from source frame); f - if specified generate frame in destination channel even if frame with " +
                    "such coordinates already exist." +
                    "Examples: \"-g 0:2:\" - find in the 2th channel(DownScan) frames with coordinates " +
                    "that are absent in the 0th channel(Primary) and generate frames with such coordinates in the 0th " +
                    "channel. \"-g 1:2:5:f\" - get frames from 2th(DownScan) and 5th (SidescanComposite) channels and " +
                    "group em by unique coordinates. After that generate 1th(Secondary) channel frames (even frames with " +
                    "coordinates from 2th and 5th channel are in 1th(\"f\" option)). \"-g 0:5:d\" - get frames from 5th " +
                    "(SidescanComposite) channel and generate frames (with generated by depth(\"d\" option) SoundedData) for 0th(Primary) " +
                    "channel if they are not already exist.")]
            public IList<string> GenegateParams { get; set; }

            [Usage(ApplicationAlias = "dotnet ConsoleLogConverter.Core.dll")]
            public static IEnumerable<Example> Examples
            {
                get
                {
                    yield return new Example($"Log file format conversion example.{Environment.NewLine} Command takes all frames from input.sl2. At the next step it subtract (use \"m\"(minus) prefix to substract and \"p\"(plus) to add value) 1.15 meters from depth value at each frame and finally saves frames to \"csv\" format",
                        new Options { InputFile = "input.sl2", FramesFrom = 10, FramesTo =509, Channels = new List<string>(){"0"}, CoordinatesDelete = true, OutputFileVersion = new List<string>() { "sl2","csv" } });

                    yield return new Example($"Manual depth shift example.{Environment.NewLine} Command takes all frames from input.sl2. At the next step it subtract (use \"m\"(minus) prefix to substract and \"p\"(plus) to add value) 1.15 meters from depth value at each frame and finally saves frames to \"csv\" format",
                        new Options { InputFile = "input.sl2", DepthShift = "m1.15", OutputFileVersion = new List<string>() { "csv" } });

                    yield return new Example($"Depth adjust example.{Environment.NewLine} There are situations where the depth in one log is necessary to adjust to the depth in another log. Command takes all frames from BaseDepthPoints.sl2 and pointsForAdjust.sl2 files. At the next step it finds nearest points at two sequences and calculate depth difference between them. After that it add difference to each pointsForAdjust.sl2 frame. Finally it contact two sequences and save frames to file with \"csv\"",
                        new Options { InputFile = "BaseDepthPoints.sl2", DepthAdjustFile = "pointsForAdjust.sl2", OutputFileVersion = new List<string>() { "csv" } });

                    yield return new Example($"Channel generation example.{Environment.NewLine} There are situations where you have valid data at one channel and corrupt data at another. You can generate frames for specific(corrupted) channel from the frames at other(valid) channel(s). Command takes all frames from 2th(DownScan) and 5th (SidescanComposite) channels from input.sl2 and group them by unique coordinates. After that it generate 1th(Secondary) channel frames (even if frames with such coordinates from 2th and 5th chanel are in 1th(\"f\" option)). f - if specified generate frame in destination channel even if frame with such coordinates already exist; d - if specified generate sounded data from source frame depth value,(otherwise take sounded data from source frame)",
                        new Options { InputFile = "input.sl2", GenegateParams = new List<string>() { "1", "2", "5", "f" }, OutputFileVersion = new List<string>() { "sl2" } });

                    yield return new Example($"Flip Sidescan view example.{Environment.NewLine} If you have confused with position of the transducer and fixed the back to the front, then you have got the reversed sidescan images. You can fix it and flip it back. Command takes all frames from input.sl2. At the next step it flip sounded data in frames at 5th(SidescanComposite) channel and finally save frames to \"sl2\" format.",
                        new Options { InputFile = "input.sl2", FlipSoundedData = true, OutputFileVersion = new List<string>() { "sl2" } });

                    yield return new Example($"{Environment.NewLine}Research command example.{Environment.NewLine}SL binary formats are closed, and there is for all values no public schema. So, you can help to project and make some research by yourself.Command takes all frames from input.sl3. At the next step it takes frames from channels 0 and 2 with frame index from 0 to 10. And finally it takes four bytes at 30 offset from each frame start and represent them as differet types of value(string, single bytes, short from first two bytes, short from second two bytes, integer, float).",
                        new Options { InputFile = "input.sl3", SearchOffset = 30, FramesTo = 10, Channels = new List<string> { "0", "2" } });
                }
            }
        }

        private static void LowranceLogDataInfo(LowranceLogData data)
        {
            if (data == null)
                return;
            Console.WriteLine("File Version: {0}, Hardware Version: {1}, Block Size: {2}", data.Header.FileVersion, data.Header.HardwareVersion, data.Header.BlockSize);
            Console.WriteLine("File creation time: {0:dd.MM.yyyy HH:mm:ss zzz}\n", data.CreationDateTime);

            var tableHeader = $"|{"Channel Type",20}|{"Frequency",22}|{"First Frame №",13}|{"Last Frame №",12}|{"Frames Total",12}|";

            Console.WriteLine(tableHeader);
            Console.WriteLine("-------------------------------------------------------------------------------------");

            foreach (var channel in data.Frames.Select(fr => fr.ChannelType).Distinct())
            {
                var channelFrames = data.Frames.Where(fr => fr.ChannelType == channel).ToList();
                var firstFrame = channelFrames.FirstOrDefault();

                var str = $"|{channel + "(" + (byte)channel + ")",20}|{firstFrame?.Frequency,22}|{firstFrame?.FrameIndex,13}|{channelFrames.Last().FrameIndex,12}|{channelFrames.Count,12}|";
                Console.WriteLine(str);

            }
            Console.WriteLine("-------------------------------------------------------------------------------------");
            var lastStr = $"|{"",20}|{"",22}|{"",13}|{"",12}|{data.Frames.Count,12}|";
            Console.WriteLine(lastStr);
        }

        private static LowranceLogData ReadFile(string filename, bool verbose)
        {
            var stopWatch = new Stopwatch();
            if (verbose)
                Console.WriteLine("Reads file: {0}", filename);

            LowranceLogData data;
            try
            {
                using (var stream = new FileStream(filename, FileMode.Open, FileAccess.Read))
                {
                    stopWatch.Start();
                    data = LowranceLogData.ReadFromStream(stream);
                    stopWatch.Stop();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return null;
            }

            if (verbose)
            {
                Console.WriteLine("Read time: " + stopWatch.Elapsed + "\n");
                stopWatch.Reset();
                LowranceLogDataInfo(data);
            }

            return data;
        }


        public static bool DepthShiftTryParse(string inputString, out double value)
        {
            value = double.NaN;
            var nfi = new NumberFormatInfo
            {
                NegativeSign = "m",
                PositiveSign = "p"
            };

            try
            {
                value = double.Parse(inputString, nfi);
            }
            catch (Exception e)
            {
                Console.WriteLine("Can't parse Depth Shift. Full error message:\"{0}\"", e);
            }

            return !double.IsNaN(value);
        }

        static void Main(string[] args)
        {
            var parserResult = Parser.Default.ParseArguments<Options>(args);

            if (parserResult.Tag == ParserResultType.Parsed)
            {
                var options = ((Parsed<Options>)parserResult).Value;
                //enlarge console window width
                Console.WindowWidth = 120;

                //convert channels from string to enum
                var outputchannels = new List<ChannelType>();
                if (options.Channels != null && options.Channels.Any())
                {
                    outputchannels.AddRange(options.Channels.Select(channel => (ChannelType)Enum.Parse(typeof(ChannelType), channel)));
                }

                var stopWatch = new Stopwatch();

                //Read Files
                var data = ReadFile(options.InputFile, options.Verbose);

                if (data == null)
                {
                    Console.WriteLine("Can't read frames from file");
                    Console.WriteLine($"{Environment.NewLine}Please press any key to exit...");
                    Console.ReadKey(true);
                    return;
                }

                #region Depth Adjust

                if (!string.IsNullOrWhiteSpace(options.DepthAdjustFile))
                {
                    var adjust = ReadFile(options.DepthAdjustFile, options.Verbose);
                    var da = new DepthAdjuster(data.Frames, adjust.Frames);

                    if (options.Verbose)
                    {
                        Console.WriteLine("Depth adjust option enabled.");
                        Console.WriteLine("Adjust file name:\"{0}\".", options.DepthAdjustFile);

                        da.NearestPointsFound += (o, e) =>
                        {
                            Console.WriteLine("Nearest points are:\nBase - {0} with {1} depth.\nAdjust - {2} with {3}.",
                                              e.FirstPoint.Point, e.FirstPoint.Depth, e.SecondPoint.Point, e.SecondPoint.Depth);
                            Console.WriteLine("Distance = {0}", e.Distance);
                        };

                        Console.WriteLine("Looking for the nearest points.....");
                    }

                    stopWatch.Start();
                    var points = da.AdjustDepth();

                    if (options.Verbose)
                    {
                        Console.WriteLine("Adjust time: " + stopWatch.Elapsed + "\n");
                    }
                    stopWatch.Reset();

                    //add points to original sequence
                    foreach (var point in points)
                    {
                        data.Frames.Add((Frame)point);
                    }

                }
                #endregion

                #region Depth Shift

                //add or substrate depth shift for all frames in data
                if (!string.IsNullOrWhiteSpace(options.DepthShift))
                {
                    if (options.Verbose)
                    {
                        Console.WriteLine("Depth shift option enabled.");
                        Console.WriteLine("Trying parse depth shift value...");
                    }

                    if (DepthShiftTryParse(options.DepthShift, out var value))
                    {
                        if (options.Verbose) Console.WriteLine("Depth shift value is:{0}", value);

                        //applying depth shift for all frames in data
                        foreach (var frame in data.Frames)
                        {
                            frame.Depth = new LinearDimension(frame.Depth.GetMeters() + value, LinearDimensionUnit.Meter);
                        }
                    }

                }
                #endregion

                #region Generate channels frames
                //check generate parameters
                if (options.GenegateParams != null && options.GenegateParams.Any())
                {
                    if (options.Verbose)
                    {
                        Console.WriteLine("Generate frames for specific channel option enabled.");
                        Console.WriteLine("Try parse parameters...");
                    }
                    var dstChannel = new List<ChannelType>();
                    var sourceChannels = new List<ChannelType>();
                    var forceGenerate = false;
                    var generateFromDepth = false;
                    foreach (var optionsGenegateParam in options.GenegateParams)
                    {
                        if (Enum.TryParse<ChannelType>(optionsGenegateParam, out var channelType))
                        {
                            if (!dstChannel.Any()) dstChannel.Add(channelType);
                            else sourceChannels.Add(channelType);
                        }
                        if (string.Compare(optionsGenegateParam, "f", StringComparison.InvariantCultureIgnoreCase) == 0)
                            forceGenerate = true;
                        if (string.Compare(optionsGenegateParam, "d", StringComparison.InvariantCultureIgnoreCase) == 0)
                            generateFromDepth = true;
                    }

                    if (options.Verbose)
                    {
                        Console.Write("Source channel(s) are:");
                        foreach (var sourceChannel in sourceChannels)
                        {
                            Console.Write(sourceChannel + ",");
                        }
                        Console.Write("\n");
                        Console.WriteLine("Channel for frame generation is:{0}", dstChannel.Single());

                        Console.WriteLine("Force erase points at destination channel before generate(\"f\" option): {0}", forceGenerate);
                        Console.WriteLine("Generate sounded data from depth value(\"d\" option): {0}", generateFromDepth);

                        if (!sourceChannels.Any()) Console.WriteLine("There is no channels for frame generation. Skip generation option.");
                    }

                    //continue if sourceChannels.Any()
                    if (sourceChannels.Any())
                    {
                        //get unique frames(by FrameIndex) from source channel(s) 
                        var unicueFrameFromSourceChanels = data.Frames
                            .Where(frame => sourceChannels.Contains(frame.ChannelType))
                            .GroupBy(frame => frame.FrameIndex)
                            .Select(group => group.First())
                            .ToList();

                        var erasedPointsCountAtDstChannel = 0;
                        //erase dstChannel frames from data
                        if (forceGenerate)
                        {
                            erasedPointsCountAtDstChannel = data.Frames.RemoveAll(frame => frame.ChannelType == dstChannel.Single());
                        }

                        //get points for existed dst channel frames
                        var dstChannelFramesPoints = data.Frames
                            .Where(frame => frame.ChannelType == dstChannel.Single())
                            .Select(frame => frame.Point).ToList();

                        //generate and add frame for each unique point
                        foreach (var unicueFrame in unicueFrameFromSourceChanels)
                        {
                            if (!dstChannelFramesPoints.Contains(unicueFrame.Point))
                            {
                                data.Frames.Add(Frame.GenerateFromOtherChannelFrame(dstChannel.Single(), unicueFrame, generateFromDepth));
                            }
                        }

                        if (options.Verbose)
                        {
                            Console.WriteLine("Unique points at source channel(s):{0}", unicueFrameFromSourceChanels.Count);
                            if (forceGenerate)
                                Console.WriteLine("Points erased at destination channel:{0}", erasedPointsCountAtDstChannel);
                            Console.WriteLine("Points added to destination channel:{0}\n", unicueFrameFromSourceChanels.Count - dstChannelFramesPoints.Count);
                        }
                    }
                }


                #endregion

                #region Flip SoundedData

                if (options.FlipSoundedData)
                {
                    if (options.Verbose)
                    {
                        Console.WriteLine("Sounded Data flip option enabled.");
                        Console.WriteLine("Flipping data for SidescanComposite channel ...\n");
                    }

                    foreach (var frame in data.Frames)
                    {
                        //flip sounded data for SidescanComposite channel
                        if (frame.ChannelType == ChannelType.SidescanComposite)
                            frame.SoundedData = frame.SoundedData.FlipSoundedData();
                    }
                }

                #endregion

                #region Research mode console output

                //if research mode, then opens file again
                if (options.SearchOffset >= 0)
                {
                    Console.WriteLine("Try research values from " + options.SearchOffset + " bytes offset...\n");
                    try
                    {
                        using (var stream = new FileStream(options.InputFile, FileMode.Open, FileAccess.Read))
                        {
                            stopWatch.Start();
                            using (var reader = new BinaryReader(stream))
                            {
                                var fileHeader = Header.ReadHeader(reader, 0);

                                var researchResult = Frame.ValuesResearch(reader, Header.Lenght, options.SearchOffset, fileHeader.FileVersion);

                                var tableHeader = $"|{"String Value",12}|{"Bytes",16}|{"Short #1",8}|{"Short #2",8}|{"Integer",10}|{"Float",15}|{"Frame Index",11}|{"Channel",17}|";
                                //Console.WriteLine("String Value \t| Bytes \t| First Short \t| Second Short \t| Integer \t| Float \t| Frame Index \t| Channel");
                                Console.WriteLine(tableHeader);
                                Console.WriteLine("------------------------------------------------------------------------------------------------------------");

                                foreach (var offset in researchResult.Keys)
                                {
                                    var tuple = researchResult[offset];

                                    //skip Console.WriteLine if frame channel is not selected
                                    if (outputchannels.Any() && !outputchannels.Contains(tuple.Item7))
                                        continue;

                                    //skip Console.WriteLine if frame is not inside diapason
                                    if (tuple.Item6 < options.FramesFrom || tuple.Item6 > options.FramesTo)
                                        continue;

                                    var stringbuilder = new StringBuilder();
                                    foreach (var onebyte in tuple.Item1)
                                    {
                                        stringbuilder.Append(onebyte + ",");
                                    }

                                    var str = $"|{BitConverter.ToString(tuple.Item1),12}|{stringbuilder,16}|{tuple.Item2,8}|{tuple.Item3,8}|{tuple.Item4,10}|{tuple.Item5,15}|{tuple.Item6,11}|{tuple.Item7,17}|";

                                    Console.WriteLine(str);
                                }
                            }
                            stopWatch.Stop();
                            Console.WriteLine("Read and research time: " + stopWatch.Elapsed + "\n");
                            stopWatch.Reset();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                }

                #endregion

                #region Creating output file

                //makes output file if it necessary
                if (options.OutputFileVersion != null && options.OutputFileVersion.Any())
                {
                    bool FrameValidationFunc(Frame frame)
                    {
                        var isIndexValid = frame.FrameIndex >= options.FramesFrom && frame.FrameIndex <= options.FramesTo;

                        return outputchannels.Any() ? outputchannels.Contains(frame.ChannelType) && isIndexValid : isIndexValid;
                    }

                    Console.WriteLine("Making new frames list...\n");
                    stopWatch.Start();
                    var newFrames = data.Frames.Where(FrameValidationFunc).ToList();
                    stopWatch.Stop();
                    Console.WriteLine("List created.  Creation time: " + stopWatch.Elapsed + "\n");
                    stopWatch.Reset();


                    //delete coordinates if it necessary
                    if (options.CoordinatesDelete)
                    {
                        Console.WriteLine("Anonymize coordinate points...\n");
                        stopWatch.Start();

                        //move track to random place
                        var rnd = new Random();
                        double lat = rnd.Next(-90, 90);
                        double lon = rnd.Next(-180, 180);

                        //func for coordinate point delete
                        Frame CoordinatesDelete(Frame frame)
                        {
                            frame.Point = new CoordinatePoint(Latitude.FromDegrees(frame.Point.Latitude.ToDegrees() % 1 + lat),
                                Longitude.FromDegrees(frame.Point.Longitude.ToDegrees() % 1 + lon));
                            return frame;
                        }

                        //coordinate point delete
                        newFrames = newFrames.Select(CoordinatesDelete).ToList();
                        stopWatch.Stop();
                        Console.WriteLine("Anonymizations time: " + stopWatch.Elapsed + "\n");
                        stopWatch.Reset();
                    }

                    var newData = new LowranceLogData { Frames = newFrames };

                    //checks output formats and write to files
                    foreach (var format in options.OutputFileVersion)
                    {

                        #region Creating SL2

                        if (string.Compare(format, "sl2", StringComparison.InvariantCultureIgnoreCase) == 0)
                        {
                            //if original header have the same format then reuse it
                            newData.Header = data.Header.FileVersion == FileVersion.SL2 ? data.Header : Header.sl2;
                            try
                            {
                                using (var stream = new FileStream(@"out.sl2", FileMode.Create, FileAccess.Write))   //- мой короткий 	)
                                {
                                    Console.WriteLine("Writing \"out.sl2\" file...\n");
                                    stopWatch.Start();

                                    LowranceLogData.WriteToStream(stream, newData);

                                    stopWatch.Stop();
                                    Console.WriteLine("Writing complete.  Writing time: " + stopWatch.Elapsed + "\n");
                                    stopWatch.Reset();
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex);
                            }

                        }
                        #endregion

                        #region Creating SL3

                        if (string.Compare(format, "sl3", StringComparison.InvariantCultureIgnoreCase) == 0)
                        {
                            //if original header have the same format then reuse it
                            newData.Header = data.Header.FileVersion == FileVersion.SL3 ? data.Header : Header.sl3;
                            try
                            {
                                using (var stream = new FileStream(@"out.sl3", FileMode.Create, FileAccess.Write))   //- мой короткий 	)
                                {
                                    Console.WriteLine("Writing \"out.sl3\" file...\n");
                                    stopWatch.Start();

                                    LowranceLogData.WriteToStream(stream, newData);

                                    stopWatch.Stop();
                                    Console.WriteLine("Writing complete.  Writing time: " + stopWatch.Elapsed + "\n");
                                    stopWatch.Reset();
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex);
                            }

                        }
                        #endregion

                        #region Creating CVS

                        if (string.Compare(format, "csv", StringComparison.InvariantCultureIgnoreCase) == 0)
                        {
                            //create CVSLogData object
                            var cvsData = new CsvLogData()
                            {
                                CreationDateTime = DateTimeOffset.Now,
                                Name = "CVSLogData object",

                                //grouping points by coordinate and take point with average depth
                                Points = newFrames.GetUniqueDepthPoints()
                            };

                            //writing points to file
                            try
                            {
                                using (var stream = new FileStream(@"out.csv", FileMode.Create, FileAccess.Write))
                                {
                                    Console.WriteLine("Writing \"out.csv\" file...\n");
                                    stopWatch.Start();

                                    CsvLogData.WriteToStream(stream, cvsData);

                                    stopWatch.Stop();
                                    Console.WriteLine("{0} points writing complete.  Writing time: {1} \n", cvsData.Points.Count(), stopWatch.Elapsed);
                                    stopWatch.Reset();
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex);
                            }

                        }

                        #endregion
                    }
                }
                #endregion

            }

            Console.WriteLine($"{Environment.NewLine}Please press any key to exit...");
            Console.ReadKey(true);
        }
    }
}