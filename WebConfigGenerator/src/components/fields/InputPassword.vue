<template>
    <div class="form-item">
        <label :for="schema.field">
            {{ schema.label }}
            <span v-if="schema.required" class="req">*</span>
            <span v-if="schema.description" class="desc">{{ $t(schema.description) }}</span>
        </label>
        <input type="password" :name="schema.field" :id="schema.field" :placeholder="schema.placeholder" :required="schema.required" :class="{ error: invalid }" v-model="value">
        <span v-if="invalid" class="error">{{ errors.join(' ') }}</span>
    </div>
</template>

<script>
  import Input from '../mixin/Input.vue';

  export default {
    mixins: [Input],
    name: 'InputPassword',
    computed: {
      errors() {
        return this.validate(this.value);
      },
      valid() {
        return this.errors.length === 0;
      },
      invalid() {
        return this.errors.length !== 0;
      }
    }
  };
</script>
