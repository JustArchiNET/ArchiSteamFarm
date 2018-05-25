<template>
    <div class="form-item form-checkboxes">
        <label v-for="checkbox in schema.fields" :for="checkbox.field" class="checkbox">
            <input type="checkbox" :id="checkbox.field" :name="checkbox.field" :required="checkbox.required" v-model="value">
            {{ checkbox.label }}
            <span v-if="checkbox.required" class="req">*</span>
        </label>
    </div>
</template>

<script>
  import Input from '../mixin/Input.vue';

  export default {
    mixins: [Input],
    name: 'CheckboxGroup'
  };
</script>
