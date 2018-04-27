<template>
    <div class="form-item">
        <label :for="schema.field" class="checkbox" @click.self="value = !value">
            <button class="button small" :class="{ outline: value }" @click.prevent="value = false">✖</button>
            <button class="button small" :class="{ outline: !value }" @click.prevent="value = true">✔</button>
            {{ schema.label }}
            <span v-if="schema.required" class="req">*</span>
        </label>
    </div>
</template>

<script>
  import Input from '../mixin/Input.vue';

  export default {
    mixins: [Input],
    name: 'InputCheckbox'
  };
</script>

<style lang="scss">
    .checkbox {
        button {
            transition: all .3s;
            margin-right: 2px;

            &:last-of-type {
                margin-right: 5px;
            }
        }
    }

    .checkbox-toggle {
        height: 100%;
    }
</style>
