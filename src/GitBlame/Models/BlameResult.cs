﻿
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GitBlame.Models
{
	/// <summary>
	/// <see cref="BlameResult"/> is the result of running <c>git blame</c> on a specific revision of a specific file.
	/// </summary>
	internal sealed class BlameResult
	{
		public BlameResult(ReadOnlyCollection<Block> blocks, ReadOnlyCollection<Line> lines, Dictionary<string, Commit> commits)
		{
			m_blocks = blocks;
			m_lines = lines;
			m_commits = commits;
		}

		public ReadOnlyCollection<Block> Blocks
		{
			get { return m_blocks; }
		}

		public ReadOnlyCollection<Commit> Commits
		{
			get
			{
				return new List<Commit>(m_commits.Values).AsReadOnly();
			}
		}

		public ReadOnlyCollection<Line> Lines
		{
			get { return m_lines; }
		}

		readonly ReadOnlyCollection<Block> m_blocks;
		readonly ReadOnlyCollection<Line> m_lines;
		readonly Dictionary<string, Commit> m_commits;
	}
}
