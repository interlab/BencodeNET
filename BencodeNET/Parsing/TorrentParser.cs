﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BencodeNET.Exceptions;
using BencodeNET.IO;
using BencodeNET.Objects;
using BencodeNET.Torrents;

namespace BencodeNET.Parsing
{
    // TODO: Unit tests
    // TODO: Parse extra (non-standard) fields
    /// <summary>
    /// A parser for torrent files.
    /// </summary>
    public class TorrentParser : BObjectParser<Torrent>
    {
        /// <summary>
        /// Creates an instance using the specified <see cref="IBencodeParser"/> for parsing
        /// the torrent <see cref="BDictionary"/>.
        /// </summary>
        /// <param name="bencodeParser">The parser used for parsing the torrent <see cref="BDictionary"/>.</param>
        public TorrentParser(IBencodeParser bencodeParser)
        {
            if (bencodeParser == null) throw new ArgumentNullException(nameof(bencodeParser));

            BencodeParser = bencodeParser;
        }

        /// <summary>
        /// The parser ued for parsing the torrent <see cref="BDictionary"/>.
        /// </summary>
        protected IBencodeParser BencodeParser { get; set; }

        /// <summary>
        /// The encoding used for parsing.
        /// </summary>
        protected override Encoding Encoding => BencodeParser.Encoding;

        /// <summary>
        /// Parses the next <see cref="BDictionary"/> from the stream as a <see cref="Torrent"/>.
        /// </summary>
        /// <param name="stream">The stream to parse from.</param>
        /// <returns>The parsed <see cref="Torrent"/>.</returns>
        public override Torrent Parse(BencodeStream stream)
        {
            var data = BencodeParser.Parse<BDictionary>(stream);
            return CreateTorrent(data);
        }

        /// <summary>
        /// Creates a torrrent by reading the relevant data from the <see cref="BDictionary"/>.
        /// </summary>
        /// <param name="data">The torrent bencode data.</param>
        /// <returns>A <see cref="Torrent"/> matching the input.</returns>
        protected Torrent CreateTorrent(BDictionary data)
        {
            EnsureValidTorrentData(data);

            var info = data.Get<BDictionary>(TorrentFields.Info);

            var torrent = new Torrent
            {
                IsPrivate = info.Get<BNumber>(TorrentFields.Private) == 1,
                PieceSize = info.Get<BNumber>(TorrentFields.PieceLength),
                Pieces = info.Get<BString>(TorrentFields.Pieces)?.ToString(),

                Comment = data.Get<BString>(TorrentFields.Comment)?.ToString(),
                CreatedBy = data.Get<BString>(TorrentFields.CreatedBy)?.ToString(),
                Encoding = ParseEncoding(data.Get<BString>(TorrentFields.Encoding)),
                CreationDate = data.Get<BNumber>(TorrentFields.CreationDate),

                Trackers = LoadTrackers(data)
            };

            if (info.ContainsKey(TorrentFields.Length))
            {
                torrent.File = LoadSingleFileInfo(info);
            }
            else if (info.ContainsKey(TorrentFields.Files))
            {
                torrent.Files = LoadMultiFileInfoList(info);
            }

            return torrent;
        }

        /// <summary>
        /// Checks the torrent data for required fields and throws an exception if any are missing or invalid.
        /// </summary>
        /// <param name="data">The torrent data.</param>
        /// <exception cref="InvalidTorrentException">The torrent data is missing required fields or otherwise invalid.</exception>
        protected void EnsureValidTorrentData(BDictionary data)
        {
            if (!data.ContainsKey(TorrentFields.Info))
                throw new InvalidTorrentException("Torrent is missing 'info'-dictionary.", TorrentFields.Info);

            var info = data.Get<BDictionary>(TorrentFields.Info);

            var requiredFields = new List<string>
            {
                TorrentFields.PieceLength,
                TorrentFields.Pieces,
                TorrentFields.Name
            };

            // Single-file torrents must have either the 'length' field or the 'files' field, but not both
            if (info.ContainsKey(TorrentFields.Length) && info.ContainsKey(TorrentFields.Files))
            {
                throw new InvalidTorrentException(
                    $"Torrent 'info'-dictionary cannot contain both '{TorrentFields.Length}' and '{TorrentFields.Files}'.");
            }

            if (!info.ContainsKey(TorrentFields.Length))
                requiredFields.Add(TorrentFields.Files);

            EnsureFields(requiredFields, info, "Torrent is missing required field in 'info'-dictionary.");

            if (info.ContainsKey(TorrentFields.Files))
            {
                var filesData = info.Get<BList<BDictionary>>(TorrentFields.Files);

                var requiredFileFields = new[]
                {
                    TorrentFields.Length,
                    TorrentFields.Path
                };

                EnsureFields(requiredFileFields, filesData, "Torrent is missing required field in 'info.files' dictionaries.");
            }
        }

        private static void EnsureFields(IEnumerable<string> requiredFields, BDictionary data, string message = null)
        {
            message = message ?? "Torrent is missing required field.";

            foreach (var field in requiredFields.Where(field => !data.ContainsKey(TorrentFields.Info)))
            {
                throw new InvalidTorrentException("Torrent is missing required field.", field);
            }
        }

        private static void EnsureFields(IEnumerable<string> requiredFields, IEnumerable<BDictionary> list, string message = null)
        {
            message = message ?? "Torrent is missing required field.";

            foreach (var data in list)
            {
                foreach (var field in requiredFields.Where(field => !data.ContainsKey(TorrentFields.Info)))
                {
                    throw new InvalidTorrentException(message, field);
                }
            }
        }

        /// <summary>
        /// Loads file info for single-file torrents.
        /// </summary>
        /// <param name="info">The 'info'-dictionary of a torrent.</param>
        /// <returns>The file info.</returns>
        protected virtual SingleFileInfo LoadSingleFileInfo(BDictionary info)
        {
            return new SingleFileInfo
            {
                FileName = info.Get<BString>(TorrentFields.Name)?.ToString(),
                FileSize = info.Get<BNumber>(TorrentFields.Length),
                Md5Sum = info.Get<BString>(TorrentFields.Md5Sum)?.ToString()
            };
        }

        /// <summary>
        /// Loads file info for multi-file torrents.
        /// </summary>
        /// <param name="info">The 'info'-dictionary of a torrent.</param>
        /// <returns>The file info.</returns>
        protected virtual MultiFileInfoList LoadMultiFileInfoList(BDictionary info)
        {
            var list = new MultiFileInfoList
            {
                DirectoryName = info.Get<BString>(TorrentFields.Name).ToString(),
            };

            var fileInfos = info.Get<BList>(TorrentFields.Files).Cast<BDictionary>()
                .Select(x => new MultiFileInfo
                {
                    FileSize = x.Get<BNumber>(TorrentFields.Length),
                    Path = x.Get<BList>(TorrentFields.Path)?.AsStrings().ToList(),
                    Md5Sum = x.Get<BString>(TorrentFields.Md5Sum)?.ToString()
                });

            list.AddRange(fileInfos);

            return list;
        }

        /// <summary>
        /// Loads trackers (announce URLs) from a torrent.
        /// </summary>
        /// <param name="data">The torrent data to load trackers from.</param>
        /// <returns>A list of list of trackers (announce URLs).</returns>
        protected virtual IList<IList<string>> LoadTrackers(BDictionary data)
        {
            var trackerList = new List<IList<string>>();
            var primary = new List<string>();
            trackerList.Add(primary);

            var announce = data.Get<BString>(TorrentFields.Announce)?.ToString();
            if (!string.IsNullOrEmpty(announce))
            {
                primary.Add(announce);
            }

            var announceLists = data.Get<BList>(TorrentFields.AnnounceList)?.AsType<BList>() as IList<BList>;
            if (announceLists?.Any() == true)
            {
                primary.AddRange(announceLists.First().AsStrings());
                trackerList[0] = primary.Distinct().ToList();

                trackerList.AddRange(
                    announceLists.Skip(1)
                        .Select(x => x.AsStrings().ToList()));
            }

            return trackerList;
        }

        /// <summary>
        /// Parses the encoding string to an <see cref="Encoding"/>.
        /// Returns null if parsing fails.
        /// </summary>
        /// <param name="bstring">The <see cref="BString"/> value to parse.</param>
        /// <returns>The parsed encoding or null if parsing fails.</returns>
        protected virtual Encoding ParseEncoding(BString bstring)
        {
            if (bstring == null)
                return null;

            var str = bstring.ToString();
            try
            {
                return Encoding.GetEncoding(str);
            }
            catch (Exception)
            {
                if (string.Equals(str, "UTF8", StringComparison.OrdinalIgnoreCase))
                {
                    return Encoding.UTF8;
                }
            }

            return null;
        }
    }
}
