/*
Template Name: ASPHypelens - Responsive Bootstrap 4 Admin Template
Version: 1.1.0
Author: Sean Ngu
Website: http://www.seantheme.com/asp-Hypelens/
*/

var handleInitHighlightJs = function() {
	$('.hljs-container pre code').each(function(i, block) {
		hljs.highlightBlock(block);
	});
};


/* Controller
------------------------------------------------ */
$(document).ready(function() {
	handleInitHighlightJs();
});