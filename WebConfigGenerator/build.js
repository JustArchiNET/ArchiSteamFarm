const path = require('path');
const webpack = require('webpack');
const UglifyJSPlugin = require('uglifyjs-webpack-plugin');
const HtmlWebpackPlugin = require('html-webpack-plugin');
const { VueLoaderPlugin } = require('vue-loader');

const config = {
  mode: 'production',
  entry: {
    app: './src/app.js'
  },
  output: {
    filename: 'js/[name].js',
    chunkFilename: 'js/[id].chunk.js',
    path: path.resolve(__dirname, '../docs')
  },
  module: {
    rules: [
      {
        test: /\.vue$/,
        loader: 'vue-loader',
      },
      {
        test: /\.js$/,
        exclude: /node_modules/,
        use: {
          loader: 'babel-loader',
          options: {
            presets: ['@babel/preset-env'],
            plugins: ['@babel/plugin-syntax-dynamic-import']
          }
        }
      },
      {
        test: /\.scss$/,
        use: ['vue-style-loader', 'css-loader', 'sass-loader']
      },
      {
        test: /\.sass/,
        use: ['vue-style-loader', 'css-loader', { loader: 'sass-loader', options: { indentedSyntax: true } }]
      },
      {
        test: /\.(mp4|webm|ogg|mp3|wav|flac|aac|woff2?|eot|ttf|otf|png|jpe?g|gif|svg)(\?.*)?$/,
        use: {
          loader: 'url-loader',
          options: {
            limit: 8192,
            name: 'media/[name].[ext]'
          }
        }
      }
    ]
  },
  resolve: {
    alias: {
      vue$: 'vue/dist/vue.esm.js',
    }
  },
  plugins: [
    new HtmlWebpackPlugin({ filename: path.resolve(__dirname, '../docs/index.html'), template: 'index.html', inject: true, hash: false }),
    new UglifyJSPlugin(),
    new VueLoaderPlugin()
  ]
};

const compiler = webpack(config);

compiler.run((err, stats) => {
  if (err) {
    console.error(err.stack || err);
    if (err.details) console.error(err.details);
    return;
  }

  console.log(stats.toString({
    assets: true,
    cached: false,
    children: false,
    colors: true,
    modules: false,
    chunks: false
  }));
});
