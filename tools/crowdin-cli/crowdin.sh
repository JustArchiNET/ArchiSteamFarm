if type -p java; then
    _java=java
elif [[ -n "$JAVA_HOME" ]] && [[ -x "$JAVA_HOME/bin/java" ]];  then
    _java="$JAVA_HOME/bin/java"
else
    echo "Looks like JAVA is not installed. You can download it from https://www.java.com/"
fi
if [[ "$_java" ]]; then
    version=$("$_java" -version 2>&1 | awk -F '"' '/version/ {print $2}')
    if [[ "$version" > "1.7" ]]; then
        echo Your Java version is "$version" - OK
	sudo cp crowdin-cli.jar /usr/local/bin
	echo alias crowdin="'java -jar /usr/local/bin/crowdin-cli.jar'" >> ~/.bashrc
	echo alias crowdin="'java -jar /usr/local/bin/crowdin-cli.jar'" >> ~/.bash_profile

	if [ -f ~/.bashrc ]; then
	    . ~/.bashrc
	fi

	if [ -f ~/.bash_profile ]; then
	    . ~/.bash_profile
	fi
    else         
        echo Your Java version is "$version" - needs to be updated. You can download it from https://www.java.com/
    fi
fi
