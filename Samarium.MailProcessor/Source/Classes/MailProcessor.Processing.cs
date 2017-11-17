using System;

namespace Samarium.MailProcessor {
    using MimeKit;
    using PluginFramework;
    using Samarium.PluginFramework.Common;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;

    partial class MailProcessor {

        static object LockObject { get; } = "Knowledge is the key, and you my friend, have been locked out.";
        static bool ContinueProcessing { get; set; } = false; // Just because
        static List<Task> ProcessingTasks { get; } = new List<Task>();

        async void ProcessorLooper(object state) {
            // FIXME?
            // Currently debating whether this should be a Task
            // instead of a full-blown thread.
            // I guess it'd make more sense that way?

            const int minNumberForThreading = 100;

            Info("Preparing looper...");
            // Convert state to dynamic object
            // This object will contain all the necessary information 
            // to parse, index, and archive emails
            dynamic stateObj = state;

            ContinueProcessing = true; // Set to true to start processing

            while (ContinueProcessing) {

                // First things first: Sleep.
                // This will give the user some wiggle-room.
                Thread.Sleep(PluginConfig.GetConfig<TimeSpan>("search_interval"));

                // Occasionally check if loop should continue
                if (!ContinueProcessing)
                    break;

                // Check if max no/threads has been reached
                // If multi-threading is not used, the loop will not continue until the previous 
                // process has completed, so no need to check for that. I guess.
                var useMultiThreading = PluginConfig.GetBool("use_multithreading");
                var maxCores = PluginConfig.GetInt("core_use");
                if (useMultiThreading && maxCores == ProcessingTasks.Count) {
                    Warn("Already utilizing max. number of threads! Execution will continue once a thread becomes available.");
                    break;
                }

                // Build the file list
                Info("Building file list... please wait...");
                var fileList = (await BuildFileListAsync(PluginConfig.GetConfig<List<string>>("search_directories").ToArray())).ToList();

                // Figure out how many cores/threads to use
                var logicalCpus = Environment.ProcessorCount;
                var utilizableCores = maxCores <= logicalCpus ? maxCores : logicalCpus;
                var maxEmails = PluginConfig.GetInt("max_emails");
                
                Info($"Total available logical CPUs: { logicalCpus }");
                Info($"Utilizable CPU cores: { utilizableCores }");
                Info($"Maximum amount of emails to parse: { maxEmails }");

                // Shuffle the file list so each domain has an
                // equal chance to be processed.
                // Some emails may not make it, so 
                // let's try to make it equal.
                fileList.Shuffle();

                // Now check if the file list is greater than the
                // max configured amount of emails to parse.
                if (fileList.Count > maxEmails)
                    fileList = fileList.GetRange(0, maxEmails);

                // Great! Now that that's all sorted; we can get on to determining how many threads we'll need!
                Info("Preparing to parse {0} emails; please wait...", fileList.Count);

                {
                    // We'll scope this to make sure we don't leak anything.
                    // This has been proven to reduce the memory footprint
                    // in tests with similar applications.
                    //
                    // Let's get ready to figure out how many threads we'll require
                    var maxI = utilizableCores - ProcessingTasks.Count;

                    for (var i = 0; i < maxI; i++) {
                        // We'll need to sleep for a few seconds to offset each thread 
                        // from one another.
                        // This will prevent severe performance impairments. Trust me.

                        Thread.Sleep(new TimeSpan(0, 0, 15));
                        var start = default(int);
                        var files = default(List<FileInfo>);
                        var threadId = default(string);

                        if (i == logicalCpus - 1) {
                            // We'll do the remainder here
                            start = i * (fileList.Count / utilizableCores);
                            files = fileList.GetRange(start, (fileList.Count - start));

                            // Generate thread-related information
                            threadId = Extensions.GenerateUniqueId();

                            // Set all files to r/o
                            // This way they'll be ignored by the file list builder
                            // during the next run and we won't have
                            // any files magically disappearing
                            files.ForEach(x => x.IsReadOnly = true);

                            // TODO: Call thread
                        } else if (fileList.Count <= minNumberForThreading) {
                            // This will happen, and if this is the case
                            // there is no point in snipping this up in to multiple threads.
                            // To be perfectly honest, 1000 emails wouldn't be an
                            // issue, but this just happens to be the number I picked.
                            threadId = Extensions.GenerateUniqueId();

                            // Set all files to r/o
                            // This way they'll be ignored by the file list builder
                            // during the next run and we won't have
                            // any files magically disappearing
                            files = fileList;
                            files.ForEach(x => x.IsReadOnly = true);

                            // TODO: Call thread

                            break; // Break the loop.
                        } else {
                            // Standard mode.
                            // Essentially what we're doing here, is grabbing a number
                            // of files to be processed.
                            // The exact number of file equates to:
                            // N(files) / N(utilisable cores)
                            // Eg: 5000 files / 8 cores = 625 emails/thread
                            // The default max would be
                            // 60,000 / cores
                            // So assuming that we have eight cores to play with:
                            // 60,000 / 8 cores = 7,500 emails/core

                            files = fileList.GetRange(i * (fileList.Count / utilizableCores), (fileList.Count / utilizableCores));

                            // Set all files to r/o
                            // This way they'll be ignored by the file list builder
                            // during the next run and we won't have
                            // any files magically disappearing
                            files.ForEach(x => x.IsReadOnly = true);

                            // TODO: Call thread

                        }

                    }
                }

            }

        }

        async Task<IEnumerable<FileInfo>> BuildFileListAsync(params string[] searchDirs) => await Task.Run(() => BuildFileList(searchDirs));

        List<FileInfo> BuildFileList(params string[] searchDirs) {
            // Get domain regex
            var domainRegex = new Regex(PluginConfig.GetString("domain_regexp"), RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            var userRegex = new Regex(PluginConfig.GetString("user_regexp"), RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

            // Weed out directories that don't exist; notify the user via logs
            var nonExistantDirs = searchDirs.Where(dir => !Directory.Exists(dir));
            foreach (var nonExistantDir in nonExistantDirs.Distinct())
                Warn("Ignoring directory {0}; DOESN'T EXIST!", nonExistantDir);

            // Make sure elements in list are unique
            searchDirs = searchDirs.Where(x => !nonExistantDirs.Contains(x)).Where((Func<string, bool>)domainRegex.IsMatch).Distinct().ToArray();

            var fileList = new List<FileInfo>();

            foreach (var domainDir in searchDirs) {
                Ok("Scanning directory {0}...", domainDir);
                var dir = new DirectoryInfo(domainDir);
                var userDirs = dir.EnumerateDirectories().Where(x => userRegex.IsMatch(x.Name));
                Ok("Found {0} user directories in {1}...", userDirs.Count(), domainDir);

                foreach (var user in userDirs) {
                    Ok("Scanning user directory {0}...", user.Name);
                    var validFiles = user.EnumerateFiles().Where(

                        // Filter invalid files here
                        /************************************
                         * A file is invalid if:            *
                         * size < 128b                      *
                         * readonly = true                  *
                         * !exists                          *
                         * name is nullorwhitespace()       *
                         ************************************/

                        x => x.Exists && x.Length > 128 && !x.IsReadOnly && !string.IsNullOrWhiteSpace(x.Name)

                    ).ToList();

                    // Rename files ending with .eml to .index
                    validFiles.ForEach(file => {
                        if (file.Name.Contains(PluginConfig.GetString("post_index_file_ext"))) {
                            file.MoveTo(
                                string.Format(
                                    "{0}{1}",
                                    file.Name
                                        .Replace(PluginConfig.GetString("post_index_file_ext"), "")
                                        .Trim()
                                        .Trim('.'),
                                    PluginConfig.GetString("pre_index_file_ext")
                                )
                            );
                        }
                        fileList.Add(file);
                    });

                }

            }

            return fileList;
        }

        async Task<ElasticEmailDocument> ParseMimeMessage(FileInfo input) {

            var mimeMsg = default(MimeMessage);
            var elasticDocument = default(ElasticEmailDocument);

            // First parse the file to an understandable object
            using (var iStream = input.Open(FileMode.Open)) {
                try {
                    var parserOptions = PluginConfig.GetConfig<ParserOptions>("mime_parse_options");
                    mimeMsg = await MimeMessage.LoadAsync(parserOptions, iStream);
                } catch (Exception ex) {
                    Error("An error occurred parsing file {0}!", input.FullName);
                    Trace("Source: {0}", ex.Source);
                    Trace("Stack trace: {0}", ex.StackTrace);
                    Error("Cannot continue parsing this email!");
                    return default; // Return the default value; we will decide what happens to these later
                }
            }

            // Now let's attempt to get all of the attachments, including embedded ones


            return elasticDocument;
        }

        async Task<IEnumerable<RawEmailAttachment>> GetMimeAttachmentsAsync(MimeMessage mimeMsg) {
            var attachmentList = new List<RawEmailAttachment>();
            const string noFilename = "no_filename";

            // Loop through all attachments
            // Filter out bad ones with no content type and no file name
            foreach (var attachment in mimeMsg.BodyParts.Where(x => !string.IsNullOrEmpty(x.ContentType.Name) && !string.IsNullOrEmpty(x.ContentDisposition.FileName))) {

                var fName = attachment.ContentDisposition.FileName ?? attachment.ContentType.Name ?? noFilename;
                var contentBytes = default(byte[]);

                // Load the attachment in to a stream so we can process it correctly.
                using (var memStream = new MemoryStream()) {
                    await attachment.WriteToAsync(memStream);

                    contentBytes = memStream.ToArray();
                }

                // Now let's try the (almost) impossible and see if this attachment is a text file or not.
                if (!contentBytes.HasBinaryContent()) {
                    // Apparently no binary content was detect
                }

            }

        }

    }
}
