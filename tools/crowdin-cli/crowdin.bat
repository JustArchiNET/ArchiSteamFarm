@echo off 
IF "%CROWDIN_HOME%"=="" (ECHO crowdin is NOT defined) ELSE (java -jar "%CROWDIN_HOME%\crowdin-cli.jar" %*)
