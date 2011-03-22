﻿
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using GitBlameConsole.Utility;

namespace GitBlameConsole
{
	class Program
	{
		static void Main(string[] args)
		{
			Program program = new Program();
			try
			{
				program.Run(args);
			}
			catch (UsageException ex)
			{
				Console.Error.WriteLine("Error: {0}", ex.Message);
			}
		}

		private void Run(string[] args)
		{
			// check arguments
			if (args.Length == 0 || !File.Exists(args[0]))
				throw new UsageException(@"Usage: GitBlame path\to\file.dat");

			// run "git blame"
			string filePath = args[0];
			ExternalProcess git = new ExternalProcess(GetGitPath(), Path.GetDirectoryName(filePath));
			var results = git.Run(new ProcessRunSettings("blame", "--incremental", Path.GetFileName(filePath)));
			if (results.ExitCode != 0)
				throw new UsageException(string.Format(CultureInfo.InvariantCulture, "git blame exited with code {0}", results.ExitCode));

			// parse output
			BlameResult result = ParseBlameOutput(results.Output);

			// reproduce standard blame output (from the parsed data)
			string[] lines = File.ReadAllLines(filePath);
			foreach (Block block in result.Blocks)
			{
				for (int lineIndex = 0; lineIndex < block.LineCount; lineIndex++)
				{
					Commit commit = block.Commit;
					int fileLineIndex = block.StartLine + lineIndex;
					string line = lines[fileLineIndex - 1];
					string hash = ((commit.PreviousCommitId == null ? "^" : "") + commit.Id).WithLength(8);
					string author = commit.Author.Name.WithLength(27);
					string date = string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd HH:mm:ss zz}{1:d2}", commit.AuthorDate, Math.Abs(commit.AuthorDate.Offset.Minutes));

					string output = string.Format(CultureInfo.InvariantCulture, "{0} ({1} {2} {3,4}) {4}", hash, author, date, fileLineIndex, line);
					Console.WriteLine(output);
				}
			}
		}

		private static BlameResult ParseBlameOutput(string output)
		{
			List<Block> blocks = new List<Block>();
			Dictionary<string, Commit> commits = new Dictionary<string, Commit>();

			// read entire output of "git blame"
			using (StringReader reader = new StringReader(output))
			{
				string line;
				while ((line = reader.ReadLine()) != null)
				{
					// read beginning line in block, with format "hash origLine startLine lineCount"
					string[] components = line.Split(' ');
					string commitId = components[0];
					int originalStartLine = int.Parse(components[1], CultureInfo.InvariantCulture);
					int startLine = int.Parse(components[2], CultureInfo.InvariantCulture);
					int lineCount = int.Parse(components[3], CultureInfo.InvariantCulture);

					// read "tag value" pairs from block header, knowing that "filename" tag always ends the block
					Dictionary<string, string> tagValues = new Dictionary<string, string>();
					Tuple<string, string> tagValue;
					do
					{
						line = reader.ReadLine();
						tagValue = line.SplitOnSpace();
						tagValues.Add(tagValue.Item1, tagValue.Item2);
					} while (tagValue.Item1 != "filename");

					// check if this is a new commit
					Commit commit;
					if (!commits.TryGetValue(commitId, out commit))
					{
						commit = CreateCommit(commitId, tagValues);
						commits.Add(commitId, commit);
					}

					// add this block to the output, ordered by its StartLine
					Block block = new Block(startLine, lineCount, commit, tagValues["filename"], originalStartLine);
					int index = blocks.BinarySearch(block, new GenericComparer<Block>((l, r) => l.StartLine.CompareTo(r.StartLine)));
					Debug.Assert(index < 0, "index < 0", "New block should not already be in the list.");
					if (index < 0)
						blocks.Insert(~index, block);
				}
			}

			return new BlameResult(blocks.AsReadOnly(), commits);
		}

		// Reads known values from 'tagValues' to create a Commit object.
		private static Commit CreateCommit(string commitId, Dictionary<string, string> tagValues)
		{
			// read standard values (that are assumed to always be present)
			Person author = new Person(tagValues["author"], GetEmail(tagValues["author-mail"]));
			DateTimeOffset authorDate = ConvertUnixTime(tagValues["author-time"], tagValues["author-tz"]);
			Person committer = new Person(tagValues["committer"], GetEmail(tagValues["committer-mail"]));
			DateTimeOffset commitDate = ConvertUnixTime(tagValues["committer-time"], tagValues["committer-tz"]);
			string summary = tagValues["summary"];

			// read optional "previous" value
			string previousCommitId = null;
			string previousFileName = null;
			string previous;
			if (tagValues.TryGetValue("previous", out previous))
			{
				var hashFileName = previous.SplitOnSpace();
				previousCommitId = hashFileName.Item1;
				previousFileName = hashFileName.Item2;
			}

			return new Commit(commitId, author, authorDate, committer, commitDate, summary, previousCommitId, previousFileName);
		}

		// Converts a Unix timestamp (seconds past the epoch) and time zone to a DateTimeOffset value.
		private static DateTimeOffset ConvertUnixTime(string seconds, string timeZone)
		{
			// parse timestamp to base date time
			DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified);
			DateTime dateTime = epoch.AddSeconds(double.Parse(seconds, CultureInfo.InvariantCulture));

			// parse time zone to hours and minutes
			int hours = int.Parse(timeZone.Substring(1, 2), CultureInfo.InvariantCulture);
			int minutes = int.Parse(timeZone.Substring(3, 2), CultureInfo.InvariantCulture);
			TimeSpan offset = TimeSpan.FromMinutes((hours * 60 + minutes) * (timeZone[0] == '-' ? -1 : 1));

			return new DateTimeOffset(dateTime + offset, offset);
		}

		// Extracts the email address from a well-formed "author-mail" or "committer-mail" value.
		private static string GetEmail(string email)
		{
			return (email[0] == '<' && email[email.Length - 1] == '>') ? email.Substring(1, email.Length - 2) : email;
		}

		private static string GetGitPath()
		{
			string[] parentFolders = new[]
			{
				Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
				@"D:\Program Files (x86)",
			};

			foreach (string folder in parentFolders)
			{
				string gitPath = Path.Combine(folder, @"Git\cmd\git.cmd");
				if (File.Exists(gitPath))
					return gitPath;
			}

			throw new UsageException("Can't find msysgit installed on the system.");
		}
	}
}
