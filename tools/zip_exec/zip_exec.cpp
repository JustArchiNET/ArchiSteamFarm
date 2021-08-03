
//program to modify the executable flag of a file within a zip archive.
// useful in windows when you need to create zip files with valid executables
// directly after unpacking on linux or mac.
//i have searched all over the internet for a windows utility that does this
// but not been able to find one. all uses the file system flags for setting
// the zip flags while creating the archive. since windows keeps no such flags
// it is not possible.

//tested ok when taking a zip created by windows explorer (win7) and 7zip,
// modifying one file, and unzipping on linux using "unzip" -- this file and
// no other has become executable.

//on mac however, using the 'finder' unzip feature, more than the file given
// (perhaps all files in the central directory that are specified after the
// given file), will get the same attributes, so we need to set all other files
// to unix flags as well... this is maybe a bug in macos, but we cant fix that,
// so...

//as i see it (from the zip spec), it should be possible to have mixed
// attributes within the same zip, but apparently it is not reliable to do so.
//spec: http://www.pkware.com/documents/casestudies/APPNOTE.TXT

//this program makes no attempt to support all kinds of zip files and flags
// other than that normal for files and directories. files will be set with
// these unix flags:
//directories  drw-r--r--
//executables  -rwxr-xr-x
//normal files -rw-r--r--

//-----------------------------------------------------------------------------
//v1.00:
//unzipping files created with 7zip (and processed by this program) with windows
// explorer (but not with 7zip), produced encrypted files, although the files in
// the zip were not encrypted.
// the difference seems to be that 7zip includes directories as entries in the CD
// (empty files), while windows explorer does not.
//v1.10 (the solution for this):
//the "external attributes" were modified to combine unix and windows flags since
// apparently the upper 2 bytes are for unix and the lower 2 bytes are for
// windows. when compressing a file on mac the windows bytes were always
// 0x4000, and it lead me to believe that it was needed in the unix attributes,
// but it was not.

//i have still not found any spec for the "external attributes" on different os,
// so i relied on reverse engineering. it seems to work, and there are currently
// no known problems...?

//2021 July 7, remove non standard dependencies by including file handling code here
//#include "common.h"
//#include "fileresource.h"

//////////////////////////////////////////////
//file handling partly from fileresource.cpp and common.h
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

class C_Resource
{
public:
	C_Resource();
	~C_Resource();

	void SetMode(bool i_bReading); //default reading (resets filenames)
	bool SetFilename(const char* i_szFilename);

	bool   Read(void* i_pData, uint32_t i_iLength); //length==0xffffffff means entire resource
	void   Seek(uint32_t i_iPos, int i_iMode);
	uint32_t GetSize() { return m_iFileSize; };

	bool Write(void* i_pData, uint32_t i_iLength);

private:
	void Reset();
	bool     m_bReading;
	uint32_t m_iFileSize;
	uint32_t m_iFileStart;
	FILE* m_pFileHandle;
};

C_Resource::C_Resource()
{
	m_bReading = true;
	m_pFileHandle = NULL;
	m_iFileSize = 0;
	m_iFileStart = 0;
}

void C_Resource::Reset()
{
	if (m_pFileHandle) fclose(m_pFileHandle);
	m_pFileHandle = NULL;
	m_iFileSize = 0;
	m_iFileStart = 0;
}

C_Resource::~C_Resource()
{
	Reset();
}

void C_Resource::SetMode(bool i_bReading)
{
	m_bReading = i_bReading;
	Reset();
}

bool C_Resource::SetFilename(const char* i_szFilename)
{
	if (m_pFileHandle) Reset();

	m_pFileHandle = fopen(i_szFilename, m_bReading ? "rb" : "wb");
	if (m_pFileHandle == NULL) return false; //error opening

	fseek(m_pFileHandle, 0, SEEK_END);
	m_iFileSize = (uint32_t)ftell(m_pFileHandle); //we will never handle more than 4GB
	fseek(m_pFileHandle, 0, SEEK_SET);

	return true;
}

bool C_Resource::Read(void* i_pData, uint32_t i_iLength)
{
	bool bResult = false;
	if (m_pFileHandle && m_bReading) {
		if (i_iLength == 0) return true;
		if (i_iLength == 0xffffffff) i_iLength = (m_iFileStart + m_iFileSize)
			- (uint32_t)ftell(m_pFileHandle); //we will never handle more than 4GB
		if (fread(i_pData, i_iLength, 1, m_pFileHandle) == 1) bResult = true;
	}
	return bResult;
}

bool C_Resource::Write(void* i_pData, uint32_t i_iLength)
{
	bool bResult = false;
	if (m_pFileHandle && !m_bReading) {
		if (i_iLength == 0) return true;
		if (fwrite(i_pData, i_iLength, 1, m_pFileHandle) == 1) bResult = true;
	}
	return bResult;
}

void C_Resource::Seek(uint32_t i_iPos, int i_iMode)
{
	if (m_pFileHandle) {
		switch (i_iMode) {
		case SEEK_SET: fseek(m_pFileHandle, m_iFileStart + i_iPos, i_iMode); break;
		case SEEK_CUR: fseek(m_pFileHandle, i_iPos, i_iMode); break;
		case SEEK_END: fseek(m_pFileHandle, (m_iFileStart + m_iFileSize) - i_iPos, SEEK_SET); break;
		}
	}
}
//////////////////////////////////////////////
//zip handling

#pragma pack(2)
struct S_CentralDirectoryEntry
{
	uint32_t sign;        // 0  4 Central directory file header signature = 0x02014b50
	uint16_t ver;         // 4  2 Version made by
	uint16_t ver_needed;  // 6  2 Version needed to extract (minimum)
	uint16_t gp_flag;     // 8  2 General purpose bit flag
	uint16_t c_method;    //10  2 Compression method
	uint16_t lm_time;     //12  2 File last modification time
	uint16_t lm_date;     //14  2 File last modification date
	uint32_t crc32;       //16  4 CRC-32
	uint32_t c_size;      //20  4 Compressed size
	uint32_t u_size;      //24  4 Uncompressed size
	uint16_t name_len;    //28  2 File name length (n)
	uint16_t extra_len;   //30  2 Extra field length (m)
	uint16_t comment_len; //32  2 File comment length (k)
	uint16_t dn_start;    //34  2 Disk number where file starts
	uint16_t int_attr;    //36  2 Internal file attributes
	uint32_t ext_attrib;  //38  4 External file attributes
	uint32_t offset;      //42  4 Relative offset of local file header. This is the number of bytes between the start of the first disk on which the file occurs, and the start of the local file header. This allows software reading the central directory to locate the position of the file inside the ZIP file.
	//46      n File name
	//46+n    m Extra field
	//46+n+m  k File comment
};

struct S_CentralDirectoryEnd
{
	uint8_t  sign[4];     // 0  4 End of central directory signature = 0x06054b50
	uint16_t num_discs;   // 4  2 Number of this disk
	uint16_t cd_disc;     // 6  2 Disk where central directory starts
	uint16_t cd_num;      // 8  2 Number of central directory records on this disk
	uint16_t cd_tot_num;  //10  2 Total number of central directory records
	uint32_t cd_size;     //12  4 Size of central directory (bytes)
	uint32_t cd_start;    //16  4 Offset of start of central directory, relative to start of archive
	uint16_t comment_len; //20  2 Comment length (n)
	//22  n  Comment
};
#pragma pack()

class C_ZipFile
{
public:
	C_ZipFile();
	~C_ZipFile();

	bool Open(char *i_szZipFile);
	bool Save(char *i_szZipFile);

	bool IsDirectory(char *i_szFullFileName);
	bool IsNormal(char *i_szFullFileName);
	bool IsExecutable(char *i_szFullFileName);

	bool SetDirectory(char *i_szFullFileName);
	bool SetNormal(char *i_szFullFileName);
	bool SetExecutable(char *i_szFullFileName);

	//helpers
	static void SwapFromLittleEndian(void *i_pData, int i_iNumBytes);
	static void SwapToLittleEndian(void *i_pData, int i_iNumBytes);
private:
	void Free();
	int FindFileIndexInCD(char *i_szFile);

	bool m_bOpenOK;

	uint8_t *m_pZipMem;
	//local copy of zip header data
	S_CentralDirectoryEnd m_stCDEnd, m_stCDEndReadable;
	char *m_szZipComment;
	int m_iNumFiles;
	S_CentralDirectoryEntry *m_pCDEntries, *m_pCDEntriesReadable;
	char **m_szFilenames;
	char **m_szExtra;
	char **m_szComments;
};

C_ZipFile::C_ZipFile()
{
	m_pCDEntriesReadable = NULL;
	m_pCDEntries   = NULL;
	m_pZipMem      = NULL;
	m_iNumFiles    = 0;
	m_szZipComment = NULL;
	m_szFilenames  = NULL;
	m_szExtra      = NULL;
	m_szComments   = NULL;
	m_bOpenOK      = false;
}

C_ZipFile::~C_ZipFile()
{
	Free();
}

void C_ZipFile::Free()
{
	delete[] m_pCDEntriesReadable; m_pCDEntriesReadable = NULL;
	delete[] m_pCDEntries;   m_pCDEntries   = NULL;
	delete[] m_pZipMem;      m_pZipMem      = NULL;
	delete[] m_szZipComment; m_szZipComment = NULL;
	for(int i=0; i<m_iNumFiles; i++) {
		delete[] m_szFilenames[i];
		delete[] m_szExtra[i];
		delete[] m_szComments[i];
	}
	delete[] m_szFilenames;  m_szFilenames  = NULL;
	delete[] m_szExtra;      m_szExtra      = NULL;
	delete[] m_szComments;   m_szComments   = NULL;

	m_iNumFiles = 0;
	m_bOpenOK = false;
}

bool C_ZipFile::Open(char *i_szZipFile)
{
	Free();
	bool bResult = false;
	C_Resource *pclRes = new C_Resource();
	if(pclRes->SetFilename(i_szZipFile)) {
		int iLen, iExtraOffset;
		uint32_t iSize = pclRes->GetSize();
		m_pZipMem = new uint8_t[iSize];
		pclRes->Read(m_pZipMem, iSize); //read the entire file to memory

		//scan for CD marker (50 4b 05 06)
		bool bFound = false;
		int iPos = iSize-22;
		while(iPos>0) {
			if(m_pZipMem[iPos]==0x50 && m_pZipMem[iPos+1]==0x4b && m_pZipMem[iPos+2]==0x05 && m_pZipMem[iPos+3]==0x06) {
				//marker found, copy it
				memcpy(&m_stCDEnd,         m_pZipMem+iPos, sizeof(m_stCDEnd));
				memcpy(&m_stCDEndReadable, m_pZipMem+iPos, sizeof(m_stCDEnd));
				SwapFromLittleEndian(&m_stCDEndReadable.sign, 4);
				SwapFromLittleEndian(&m_stCDEndReadable.num_discs, 2);
				SwapFromLittleEndian(&m_stCDEndReadable.cd_disc, 2);
				SwapFromLittleEndian(&m_stCDEndReadable.cd_num, 2);
				SwapFromLittleEndian(&m_stCDEndReadable.cd_tot_num, 2);
				SwapFromLittleEndian(&m_stCDEndReadable.cd_size, 4);
				SwapFromLittleEndian(&m_stCDEndReadable.cd_start, 4);
				SwapFromLittleEndian(&m_stCDEndReadable.comment_len, 2);
				//validate that we are at the correct position
				iLen = m_stCDEndReadable.comment_len;
				if(iPos+22+iLen==iSize) {
					m_szZipComment = new char[iLen+1];
					memcpy(m_szZipComment, m_pZipMem+iPos+22, iLen);
					m_szZipComment[iLen] = 0;
					bFound = true;
					break;
				}
			}
			iPos--;
		}
		if(!bFound) {
			printf("error: no central directory found in file\n");
			goto out;
		}

		//validate that we support this zip file
		if(m_stCDEndReadable.num_discs != 0 || m_stCDEndReadable.cd_disc != 0
			|| m_stCDEndReadable.cd_num != m_stCDEndReadable.cd_tot_num)
		{
			printf("error: multiple volume files not supported\n");
			goto out;
		}

		//read all CD entries
		m_iNumFiles = m_stCDEndReadable.cd_num;
		m_pCDEntries = new S_CentralDirectoryEntry[m_iNumFiles];
		m_pCDEntriesReadable = new S_CentralDirectoryEntry[m_iNumFiles];
		m_szFilenames = new char*[m_iNumFiles];
		m_szExtra     = new char*[m_iNumFiles];
		m_szComments  = new char*[m_iNumFiles];
		iPos = m_stCDEndReadable.cd_start;
		for(int i=0; i<m_iNumFiles; i++) {
			memcpy(&m_pCDEntries[i],         m_pZipMem+iPos, sizeof(S_CentralDirectoryEntry));
			memcpy(&m_pCDEntriesReadable[i], m_pZipMem+iPos, sizeof(S_CentralDirectoryEntry));
			SwapFromLittleEndian(&m_pCDEntriesReadable[i].sign, 4);
			SwapFromLittleEndian(&m_pCDEntriesReadable[i].ver, 2);
			SwapFromLittleEndian(&m_pCDEntriesReadable[i].ver_needed, 2);
			SwapFromLittleEndian(&m_pCDEntriesReadable[i].gp_flag, 2);
			SwapFromLittleEndian(&m_pCDEntriesReadable[i].c_method, 2);
			SwapFromLittleEndian(&m_pCDEntriesReadable[i].lm_time, 2);
			SwapFromLittleEndian(&m_pCDEntriesReadable[i].lm_date, 2);
			SwapFromLittleEndian(&m_pCDEntriesReadable[i].crc32, 4);
			SwapFromLittleEndian(&m_pCDEntriesReadable[i].c_size, 4);
			SwapFromLittleEndian(&m_pCDEntriesReadable[i].u_size, 4);
			SwapFromLittleEndian(&m_pCDEntriesReadable[i].name_len, 2);
			SwapFromLittleEndian(&m_pCDEntriesReadable[i].extra_len, 2);
			SwapFromLittleEndian(&m_pCDEntriesReadable[i].comment_len, 2);
			SwapFromLittleEndian(&m_pCDEntriesReadable[i].dn_start, 2);
			SwapFromLittleEndian(&m_pCDEntriesReadable[i].int_attr, 2);
			SwapFromLittleEndian(&m_pCDEntriesReadable[i].ext_attrib, 4);
			SwapFromLittleEndian(&m_pCDEntriesReadable[i].offset, 4);

			//read filename, extra, comment
			//46      n File name
			iExtraOffset = 0;
			iLen = m_pCDEntriesReadable[i].name_len;
			m_szFilenames[i] = new char[iLen+1];
			memcpy(m_szFilenames[i], m_pZipMem+iPos+46+iExtraOffset, iLen);
			m_szFilenames[i][iLen] = 0;
			iExtraOffset += iLen;
			//46+n    m Extra field
			iLen = m_pCDEntriesReadable[i].extra_len;
			m_szExtra[i] = new char[iLen+1];
			memcpy(m_szExtra[i], m_pZipMem+iPos+46+iExtraOffset, iLen);
			m_szExtra[i][iLen] = 0; //may not be a string, but null terminate anyway
			iExtraOffset += iLen;
			//46+n+m  k File comment
			iLen = m_pCDEntriesReadable[i].comment_len;
			m_szComments[i] = new char[iLen+1];
			memcpy(m_szComments[i], m_pZipMem+iPos+46+iExtraOffset, iLen);
			m_szComments[i][iLen] = 0;
			iExtraOffset += iLen;

			iPos += sizeof(S_CentralDirectoryEntry) + iExtraOffset;
		}

		m_bOpenOK = true;
		bResult = true;
	}
out:
	delete pclRes;
	return bResult;
}

bool C_ZipFile::Save(char *i_szZipFile)
{
	bool bResult = false;
	if(!m_bOpenOK) return false;

	C_Resource *pclFile = new C_Resource();
	pclFile->SetMode(false);
	if(pclFile->SetFilename(i_szZipFile)) {
		bResult = pclFile->Write(m_pZipMem, m_stCDEndReadable.cd_start); //write all up until the CD (same as source)
		//CD entries
		for(int i=0; i<m_iNumFiles; i++) {
			//set unix normal for all non executable files.
			//this is needed for the mac 'finder' problem mentioned above...
			//it seems it cannot handle windows attributes mixed with unix attributes
			// in a zip correctly, so we need to change all files to unix attributes.
			if(IsDirectory(m_szFilenames[i])) SetDirectory(m_szFilenames[i]);
			else if(!IsExecutable(m_szFilenames[i])) SetNormal(m_szFilenames[i]);

			if(bResult) bResult = pclFile->Write(&m_pCDEntries[i], sizeof(S_CentralDirectoryEntry));
			if(bResult) bResult = pclFile->Write(m_szFilenames[i], m_pCDEntriesReadable[i].name_len);
			if(bResult) bResult = pclFile->Write(m_szExtra[i], m_pCDEntriesReadable[i].extra_len);
			if(bResult) bResult = pclFile->Write(m_szComments[i], m_pCDEntriesReadable[i].comment_len);
			if(!bResult) break;
		}
		//CD end + zip comment
		if(bResult) bResult = pclFile->Write(&m_stCDEnd, sizeof(m_stCDEnd));
		if(bResult) bResult = pclFile->Write(m_szZipComment, m_stCDEndReadable.comment_len);
	}
	delete pclFile; //closes file

	return bResult;
}

bool C_ZipFile::IsExecutable(char *i_szFullFileName)
{
	int iIdx = FindFileIndexInCD(i_szFullFileName);
	if(iIdx>=0) {
		bool bIsExec = (m_pCDEntriesReadable[iIdx].ver&0xff00)==0x0300;
		if(bIsExec) bIsExec = (m_pCDEntriesReadable[iIdx].ver_needed&0xff00)==0x0300;
		if(bIsExec) bIsExec = (m_pCDEntriesReadable[iIdx].ext_attrib&0xffff0000)==0x81ed0000; //this should represent rwx r-x r-x?
		return bIsExec;
	}
	return false;
}

bool C_ZipFile::IsNormal(char *i_szFullFileName)
{
	int iIdx = FindFileIndexInCD(i_szFullFileName);
	if(iIdx>=0) {
		bool bIsNorm = (m_pCDEntriesReadable[iIdx].ver&0xff00)==0x0300;
		if(bIsNorm) bIsNorm = (m_pCDEntriesReadable[iIdx].ver_needed&0xff00)==0x0300;
		if(bIsNorm) bIsNorm = ((m_pCDEntriesReadable[iIdx].ext_attrib&0xffff0000)!=0x41ed0000 && (m_pCDEntriesReadable[iIdx].ext_attrib&0xffff0000)!=0x81ed0000); //mac
		if(!bIsNorm) bIsNorm = (m_pCDEntriesReadable[iIdx].ext_attrib&0x00000020)==0x00000020; //win
		return bIsNorm;
	}
	return false;
}

bool C_ZipFile::IsDirectory(char *i_szFullFileName)
{
	int iIdx = FindFileIndexInCD(i_szFullFileName);
	if(iIdx>=0) {
		bool bIsDir = i_szFullFileName[strlen(i_szFullFileName)-1] == '/'; //test that should cover all different flags
		//but in case it does not
		if(!bIsDir) bIsDir = (m_pCDEntriesReadable[iIdx].ext_attrib&0x00000010)==0x00000010; //win
		if(!bIsDir) bIsDir = (m_pCDEntriesReadable[iIdx].ext_attrib&0xffff0000)==0x41ed0000; //mac
		return bIsDir;
	}
	return false;
}

bool C_ZipFile::SetExecutable(char *i_szFullFileName)
{
	int iIdx = FindFileIndexInCD(i_szFullFileName);
	if(iIdx>=0) {
		m_pCDEntriesReadable[iIdx].ver &= 0x00ff; //keep lower byte
		m_pCDEntriesReadable[iIdx].ver |= 0x0300; //set unix
		m_pCDEntries[iIdx].ver = m_pCDEntriesReadable[iIdx].ver;
		SwapToLittleEndian(&m_pCDEntries[iIdx].ver, 2);
		m_pCDEntriesReadable[iIdx].ver_needed &= 0x00ff; //keep lower byte
		m_pCDEntriesReadable[iIdx].ver_needed |= 0x0300; //set unix
		m_pCDEntries[iIdx].ver_needed = m_pCDEntriesReadable[iIdx].ver_needed;
		SwapToLittleEndian(&m_pCDEntries[iIdx].ver_needed, 2);

//		m_pCDEntriesReadable[iIdx].ext_attrib = 0x81ed4000; //this should represent rwx r-x r-x?
//		^from zip file packed with mac finder. if unpacked with windows explorer all files are displayed with green, indicating encrypted files.
//		the bit 0x00004000 should not be there according to tests i done, still have no spec
		m_pCDEntriesReadable[iIdx].ext_attrib = 0x81ed0020; //this should represent rwx r-x r-x for both unix and windows?
		m_pCDEntries[iIdx].ext_attrib = m_pCDEntriesReadable[iIdx].ext_attrib;
		SwapToLittleEndian(&m_pCDEntries[iIdx].ext_attrib, 4);
		return true;
	}
	return false;
}

bool C_ZipFile::SetNormal(char *i_szFullFileName)
{
	int iIdx = FindFileIndexInCD(i_szFullFileName);
	if(iIdx>=0) {
		m_pCDEntriesReadable[iIdx].ver &= 0x00ff; //keep lower byte
		m_pCDEntriesReadable[iIdx].ver |= 0x0300; //set unix
		m_pCDEntries[iIdx].ver = m_pCDEntriesReadable[iIdx].ver;
		SwapToLittleEndian(&m_pCDEntries[iIdx].ver, 2);
		m_pCDEntriesReadable[iIdx].ver_needed &= 0x00ff; //keep lower byte
		m_pCDEntriesReadable[iIdx].ver_needed |= 0x0300; //set unix
		m_pCDEntries[iIdx].ver_needed = m_pCDEntriesReadable[iIdx].ver_needed;
		SwapToLittleEndian(&m_pCDEntries[iIdx].ver_needed, 2);

//		m_pCDEntriesReadable[iIdx].ext_attrib = 0x81a44000; //this should represent rw- r-- r--?
//		^from zip file packed with mac finder. if unpacked with windows explorer all files are displayed with green, indicating encrypted files.
//		the bit 0x00004000 should not be there according to tests i done, still have no spec
		m_pCDEntriesReadable[iIdx].ext_attrib = 0x81a40020; //this should represent rw- r-- r-- for both unix and windows?
		m_pCDEntries[iIdx].ext_attrib = m_pCDEntriesReadable[iIdx].ext_attrib;
		SwapToLittleEndian(&m_pCDEntries[iIdx].ext_attrib, 4);
		return true;
	}
	return false;
}

bool C_ZipFile::SetDirectory(char *i_szFullFileName)
{
	int iIdx = FindFileIndexInCD(i_szFullFileName);
	if(iIdx>=0) {
		m_pCDEntriesReadable[iIdx].ver &= 0x00ff; //keep lower byte
		m_pCDEntriesReadable[iIdx].ver |= 0x0300; //set unix
		m_pCDEntries[iIdx].ver = m_pCDEntriesReadable[iIdx].ver;
		SwapToLittleEndian(&m_pCDEntries[iIdx].ver, 2);
		m_pCDEntriesReadable[iIdx].ver_needed &= 0x00ff; //keep lower byte
		m_pCDEntriesReadable[iIdx].ver_needed |= 0x0300; //set unix
		m_pCDEntries[iIdx].ver_needed = m_pCDEntriesReadable[iIdx].ver_needed;
		SwapToLittleEndian(&m_pCDEntries[iIdx].ver_needed, 2);

//		m_pCDEntriesReadable[iIdx].ext_attrib = 0x41ed4000; //this should represent drw- r-- r--?
//		^from zip file packed with mac finder. if unpacked with windows explorer all files are displayed with green, indicating encrypted files.
//		the bit 0x00004000 should not be there according to tests i done, still have no spec
		m_pCDEntriesReadable[iIdx].ext_attrib = 0x41ed0010; //this should represent drw- r-- r-- for both unix and windows?
		m_pCDEntries[iIdx].ext_attrib = m_pCDEntriesReadable[iIdx].ext_attrib;
		SwapToLittleEndian(&m_pCDEntries[iIdx].ext_attrib, 4);
		return true;
	}
	return false;
}

int C_ZipFile::FindFileIndexInCD(char *i_szFile)
{
	if(m_bOpenOK) {
		for(int i=0; i<m_iNumFiles; i++) {
			if(strcmp(m_szFilenames[i], i_szFile) ==0) return i;
		}
	}
	return -1;
}

void C_ZipFile::SwapToLittleEndian(void *i_pData, int i_iNumBytes)
{
	//do nothing on intel (therefore commented)
	/*uint8_t *dst = (uint8_t *)i_pData;
	uint8_t bytes[4];
	memcpy(bytes, i_pData, i_iNumBytes);
	switch(i_iNumBytes) {
		case 4:
			*dst++ = bytes[0]; *dst++ = bytes[1]; *dst++ = bytes[2]; *dst++ = bytes[3];
			break;
		case 2:
			*dst++ = bytes[0]; *dst++ = bytes[1];
			break;
	}*/
}

void C_ZipFile::SwapFromLittleEndian(void *i_pData, int i_iNumBytes)
{
	//do nothing on intel (therefore commented)
	/*uint8_t *dst = (uint8_t *)i_pData;
	uint8_t bytes[4];
	memcpy(bytes, i_pData, i_iNumBytes);
	switch(i_iNumBytes) {
		case 4:
			*dst++ = bytes[3]; *dst++ = bytes[2]; *dst++ = bytes[1]; *dst++ = bytes[0];
			break;
		case 2:
			*dst++ = bytes[1]; *dst++ = bytes[0];
			break;
	}*/
}

/////////////

bool FixZipFlags(char *i_szZipFile, char *i_szNewZipFile, char *i_szFileToFix)
{
	bool bResult = false; //assume error

	C_ZipFile *pclZip = new C_ZipFile();
	//open zip
	if(pclZip->Open(i_szZipFile)) {
		//change the given file to unix executable, other files and directories will
		// be set to unix attributes as well.
		if(!pclZip->SetExecutable(i_szFileToFix)) {
			printf("error: could not set \"%s\" executable\n", i_szFileToFix);
			goto out;
		}

		//save changed zip
		if(!pclZip->Save(i_szNewZipFile)) {
			printf("error: could not save output zip file (%s)\n", i_szNewZipFile);
			goto out;
		}
		bResult = true;
	}

out:
	delete pclZip;
	return bResult;
}

int main(int argc, char *argv[])
{
	printf("zip_exec v1.20\n");
#ifndef _DEBUG
	if(argc<3) {
		printf("usage: 'zip_exec \"file_with_full_path.zip\" \"file_in_archive_to_modify_with_full_path\"'\n");
		return 1;
	}

	bool bResult = FixZipFlags(argv[1], argv[1], argv[2]);
#else
	//debug
	bool bResult = FixZipFlags("d:\\galaxyv2_1.75_linux_bin.zip", "d:\\test.zip", "galaxyv2_1.75_linux_bin/galaxyv2.exe");
#endif
	if(!bResult) {
		printf("error: failed operation\n");
	}
	return bResult ? 0 : 1;
}
