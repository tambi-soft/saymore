// ---------------------------------------------------------------------------------------------
#region // Copyright (c) 2010, SIL International. All Rights Reserved.
// <copyright from='2010' to='2010' company='SIL International'>
//		Copyright (c) 2010, SIL International. All Rights Reserved.
//
//		Distributable under the terms of either the Common Public License or the
//		GNU Lesser General Public License, as specified in the LICENSING.txt file.
// </copyright>
#endregion
//
// File: SpongeProjectTests.cs
// Responsibility: Olson
//
// <remarks>
// </remarks>
// ---------------------------------------------------------------------------------------------
using System.IO;
using System.Xml;
using NUnit.Framework;
using SilUtils;

namespace SIL.Sponge.Model
{
	/// ----------------------------------------------------------------------------------------
	/// <summary>
	///
	/// </summary>
	/// ----------------------------------------------------------------------------------------
	[TestFixture]
	public class SpongeProjectTests : TestBase
	{
		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Runs before each test.
		/// </summary>
		/// ------------------------------------------------------------------------------------
		public override void TestSetup()
		{
			base.TestSetup();
			InitProject();
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Tests the private Create method
		/// </summary>
		/// ------------------------------------------------------------------------------------
		[Test]
		public void Create()
		{
			VerifyProject(m_prj, kTestPrjName);
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Tests the Load method
		/// </summary>
		/// ------------------------------------------------------------------------------------
		[Test]
		public void Load()
		{
			VerifyProject(m_prj, kTestPrjName);

			m_prj = SpongeProject.Load(m_prj.FullFilePath);
			VerifyProject(m_prj, kTestPrjName);
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Tests the Initializes method.
		/// </summary>
		/// ------------------------------------------------------------------------------------
		[Test]
		public void Initialize()
		{
			VerifyProject(m_prj, kTestPrjName);
			Assert.AreEqual(0, m_prj.Sessions.Count);

			var path1 = Path.Combine(m_prj.SessionsFolder, "bacon");
			var path2 = Path.Combine(m_prj.SessionsFolder, "eggs");

			Directory.CreateDirectory(path1);
			Directory.CreateDirectory(path2);

			var writer = XmlWriter.Create(Path.Combine(path1, "bacon.session"));
			writer.WriteStartElement("session");
			writer.WriteEndElement();
			writer.Close();

			writer = XmlWriter.Create(Path.Combine(path2, "eggs.session"));
			writer.WriteStartElement("session");
			writer.WriteEndElement();
			writer.Close();

			ReflectionHelper.CallMethod(m_prj, "Initialize", new[] { kTestPrjName, null });

			Assert.AreEqual(2, m_prj.Sessions.Count);
			Assert.AreEqual("bacon", m_prj.Sessions[0].Name);
			Assert.AreEqual("eggs", m_prj.Sessions[1].Name);
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Verifies the existence of the specified project.
		/// </summary>
		/// ------------------------------------------------------------------------------------
		private static void VerifyProject(SpongeProject prj, string expectedPrjName)
		{
			Assert.IsNotNull(prj);
			var expectedPath = Path.Combine(SpongeProject.ProjectsFolder, expectedPrjName);
			Assert.AreEqual(expectedPath, prj.Folder);
			Assert.IsTrue(Directory.Exists(prj.Folder));

			expectedPath = Path.Combine(prj.Folder, "People");
			Assert.AreEqual(expectedPath, prj.PeopleFolder);
			Assert.IsTrue(Directory.Exists(prj.PeopleFolder));

			expectedPath = Path.Combine(prj.Folder, "Sessions");
			Assert.AreEqual(expectedPath, prj.SessionsFolder);
			Assert.IsTrue(Directory.Exists(prj.SessionsFolder));

			expectedPath = Path.Combine(prj.Folder, kTestPrjFileName);
			Assert.AreEqual(expectedPath, prj.FullFilePath);
			Assert.IsTrue(File.Exists(prj.FullFilePath));
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Tests the GetUniquePersonName method.
		/// </summary>
		/// ------------------------------------------------------------------------------------
		[Test]
		public void GetUniquePersonName()
		{
			var name = m_prj.GetUniquePersonName();
			Assert.AreEqual("Unknown Name 01", name);

			var person = Person.CreateFromName(m_prj, name);
			person.Save();
			Assert.AreEqual("Unknown Name 02", m_prj.GetUniquePersonName());
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Tests the AddSession method
		/// </summary>
		/// ------------------------------------------------------------------------------------
		[Test]
		public void AddSession()
		{
			Assert.AreEqual(0, m_prj.SessionNames.Length);

			var session = m_prj.AddSession(kTestSessionName);
			Assert.IsNotNull(session);
			Assert.AreEqual(1, m_prj.Sessions.Count);
			Assert.AreEqual(kTestSessionName, m_prj.Sessions[0].Name);

			// Now add a session that already exists.
			Assert.AreEqual(session, m_prj.AddSession(kTestSessionName));
			Assert.AreEqual(1, m_prj.Sessions.Count);
			Assert.AreEqual(kTestSessionName, m_prj.Sessions[0].Name);
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Tests the Sessions property.
		/// </summary>
		/// ------------------------------------------------------------------------------------
		[Test]
		public void Sessions()
		{
			Assert.AreEqual(0, m_prj.SessionNames.Length);

			Assert.IsNotNull(m_prj.AddSession("Magenta"));
			Assert.IsNotNull(m_prj.AddSession("Blue"));
			Assert.IsNotNull(m_prj.AddSession("Yellow"));

			// Make sure the list is sorted.
			Assert.AreEqual(3, m_prj.SessionNames.Length);
			Assert.AreEqual("Blue", m_prj.Sessions[0].Name);
			Assert.AreEqual("Magenta", m_prj.Sessions[1].Name);
			Assert.AreEqual("Yellow", m_prj.Sessions[2].Name);

			// Make sure a file in the sessions folder doesn't get recognized as a folder.
			File.CreateText(Path.Combine(m_prj.SessionsFolder, "junk")).Close();
			Assert.AreEqual(3, m_prj.SessionNames.Length);
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Tests the UpdateSessions property.
		/// </summary>
		/// ------------------------------------------------------------------------------------
		[Test]
		public void UpdateSessions()
		{
			Assert.AreEqual(0, m_prj.SessionNames.Length);
			Assert.IsNotNull(m_prj.AddSession("lemon"));
			Assert.IsNotNull(m_prj.AddSession("lime"));
			Assert.IsNotNull(m_prj.AddSession("orange"));
			Assert.AreEqual(3, m_prj.SessionNames.Length);

			Directory.CreateDirectory(Path.Combine(m_prj.SessionsFolder, "grapefruit"));
			Directory.CreateDirectory(Path.Combine(m_prj.SessionsFolder, "guava"));

			ReflectionHelper.CallMethod(m_prj, "UpdateSessions");
			Assert.AreEqual(5, m_prj.Sessions.Count);
			Assert.AreEqual("grapefruit", m_prj.Sessions[0].Name);
			Assert.AreEqual("guava", m_prj.Sessions[1].Name);
		}

		///// ------------------------------------------------------------------------------------
		///// <summary>
		///// Tests the GetSessionFolder method
		///// </summary>
		///// ------------------------------------------------------------------------------------
		//[Test]
		//public void GetSessionFolder()
		//{
		//    Assert.IsNull(m_prj.GetSessionFolder(null));
		//    Assert.IsNull(m_prj.GetSessionFolder(string.Empty));
		//    Assert.IsNull(m_prj.GetSessionFolder(kTestSessionName));

		//    m_prj.AddSession(kTestSessionName);
		//    var path = Path.Combine(m_prj.SessionsPath, kTestSessionName);
		//    Assert.AreEqual(path, m_prj.GetSessionFolder(kTestSessionName));
		//}

		///// ------------------------------------------------------------------------------------
		///// <summary>
		///// Tests the GetSessionFiles method
		///// </summary>
		///// ------------------------------------------------------------------------------------
		//[Test]
		//public void GetSessionFiles()
		//{
		//    Assert.IsNull(m_prj.GetSessionFilesNames(null));
		//    Assert.IsNull(m_prj.GetSessionFilesNames(string.Empty));

		//    m_prj.AddSession(kTestSessionName);
		//    Assert.AreEqual(0, m_prj.GetSessionFilesNames(kTestSessionName).Length);

		//    // Create a few files in the session folder. Must use CreateText so we can
		//    // close the files because Create leaves them locked too long.
		//    var path = m_prj.GetSessionFolder(kTestSessionName);
		//    File.CreateText(Path.Combine(path, "yak.pdf")).Close();
		//    File.CreateText(Path.Combine(path, "hippo.wav")).Close();
		//    File.CreateText(Path.Combine(path, "meerkat.wma")).Close();

		//    // Get session's files and make sure they're returned sorted.
		//    var files = m_prj.GetSessionFilesNames(kTestSessionName);
		//    Assert.AreEqual(3, files.Length);
		//    Assert.AreEqual(files[0], Path.Combine(path, "hippo.wav"));
		//    Assert.AreEqual(files[1], Path.Combine(path, "meerkat.wma"));
		//    Assert.AreEqual(files[2], Path.Combine(path, "yak.pdf"));
		//}
	}
}
