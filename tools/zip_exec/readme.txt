
A program to set the executable flag of a file within a zip archive.

Useful in windows when you need to create zip files with valid
 executables directly after unpacking on linux or mac.


You may need to install vcredist_x86.exe to make it run.
It can be downloaded from
http://www.microsoft.com/download/en/details.aspx?displaylang=en&id=29

20210707: removed dependencies in source, only zip_exec.cpp needed now

Example commandline

zip_exec.exe "c:\path to zip\file.zip" "some path/some file in the zip"


---
Note also that to make it work on both mac and linux, unix attributes
 will be set on ALL files and directories.

Directories  drw-r--r--
Executables  -rwxr-xr-x
Normal files -rw-r--r--