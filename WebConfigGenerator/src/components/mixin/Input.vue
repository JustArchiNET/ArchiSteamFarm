<script>
  import Validators from '../../validators';

  export default {
    props: ['schema'],
    watch: {
      value() {
        this.$emit('update', this.value, this.schema.field);
      }
    },
    data() {
      return { value: this.schema.defaultValue };
    },
    methods: {
      validate(value, validator) {
        if (!validator && !this.schema.validator) {
          if (this.schema.required) return Validators.required(value, this.schema);
          return [];
        }

        if (!validator) return this.schema.validator(value, this.schema);
        return validator(value, this.schema);
      }
    }
  };
</script>
