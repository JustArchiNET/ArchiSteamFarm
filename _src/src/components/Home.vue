<template>
    <div class="home">
        <p class="text-justify" v-html="$t('home.topic')"> </p>

        <blockquote>
            <p>{{ $t('home.refer.content') }}</p>
            <small>{{ $t('home.refer.from') }}</small>
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
