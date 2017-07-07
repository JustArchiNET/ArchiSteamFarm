<template>
    <div class="home">
        <p class="text-justify">
            ASF Web Config Generator is small utility tool written in JavaScript, that helps you create your <a href="https://github.com/JustArchi/ArchiSteamFarm" target="_blank" rel="noopener">ASF</a> configurations.
            After downloading the configuration, move it to <var>config</var> folder inside ASF main directory.
            For full documentation refer to the <a href="https://github.com/JustArchi/ArchiSteamFarm/wiki/Configuration" target="_blank" rel="noopener">Wiki Page</a>
        </p>

        <blockquote>
            <p>ASF configuration is divided into two major ports - global (process) configuration, and configuration of every bot. Bot is a single steam account that is taking part in ASF process. In order to work, ASF needs at least one enabled bot instance. There is no process-enforced limit of supported bot instances, so you can use as many steam accounts (bots) as you want.</p>
            <small>- ASF Wiki</small>
        </blockquote>
    </div>
</template>

<script>
    export default {

    }
</script>

<style>
    .home {

    }

    .text-justify {
        text-align: justify;
    }
</style>