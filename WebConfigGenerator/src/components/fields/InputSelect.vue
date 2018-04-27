<template>
    <div class="form-item">
        <label :for="schema.field">
            {{ schema.label }}
            <span v-if="schema.required" class="req">*</span>
            <span v-if="schema.description" class="desc">{{ schema.description }}</span>
        </label>
        <select :name="schema.field" :id="schema.field" :required="schema.required" v-model="value">
            <option v-for="option in schema.options" :value="option.value">{{ $t(option.name) }}</option>
        </select>
    </div>
</template>

<script>
  import Input from '../mixin/Input.vue';

  export default {
    mixins: [Input],
    name: 'InputSelect'
  };
</script>
