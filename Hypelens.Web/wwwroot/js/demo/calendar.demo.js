/*
Template Name: ASPHypelens - Responsive Bootstrap 4 Admin Template
Version: 1.1.0
Author: Sean Ngu
Website: http://www.seantheme.com/asp-Hypelens/
*/

var handleRenderFullcalendar = function() {
	// external events
	var containerEl = document.getElementById('external-events');
  var Draggable = FullCalendarInteraction.Draggable;
	new Draggable(containerEl, {
    itemSelector: '.fc-event-link',
    eventData: function(eventEl) {
      return {
        title: eventEl.innerText,
        color: eventEl.getAttribute('data-color')
      };
    }
  });
  
  // fullcalendar
  var d = new Date();
	var month = d.getMonth() + 1;
	    month = (month < 10) ? '0' + month : month;
	var year = d.getFullYear();
	var day = d.getDate();
	var today = moment().startOf('day');
	var calendarElm = document.getElementById('calendar');
	var calendar = new FullCalendar.Calendar(calendarElm, {
		plugins: [ 'interaction', 'dayGrid', 'timeGrid', 'bootstrap' ],
    header: {
      left: 'dayGridMonth,timeGridWeek,timeGridDay',
      center: 'title',
      right: 'prev,next today'
    },
    buttonText: {
    	today:    'Today',
			month:    'Month',
			week:     'Week',
			day:      'Day'
    },
    defaultView: 'dayGridMonth',
    editable: true,
    droppable: true,
  	themeSystem: 'bootstrap',
  	eventLimit: true, // for all non-TimeGrid views
		views: {
			timeGrid: {
				eventLimit: 6 // adjust to 6 only for timeGridWeek/timeGridDay
			}
		},
  	events: [{
			title: 'Trip to London',
			start: year + '-'+ month +'-01',
			end: year + '-'+ month +'-05',
			color: COLOR_TEAL
		},{
			title: 'Meet with Jess Lau',
			start: year + '-'+ month +'-02T06:00:00',
			color: COLOR_BLUE
		},{
			title: 'Mobile Apps Brainstorming',
			start: year + '-'+ month +'-10',
			end: year + '-'+ month +'-12',
			color: COLOR_PINK
		},{
			title: 'Stonehenge, Windsor Castle, Oxford',
			start: year + '-'+ month +'-05T08:45:00',
			end: year + '-'+ month +'-06T18:00',
			color: COLOR_INDIGO
		},{
			title: 'Paris Trip',
			start: year + '-'+ month +'-12',
			end: year + '-'+ month +'-16'
		},{
			title: 'Domain name due',
			start: year + '-'+ month +'-15',
			color: COLOR_BLUE
		},{
			title: 'Cambridge Trip',
			start: year + '-'+ month +'-19'
		},{
			title: 'Visit Apple Company',
			start: year + '-'+ month +'-22T05:00:00',
			color: COLOR_GREEN
		},{
			title: 'Exercise Class',
			start: year + '-'+ month +'-22T07:30:00',
			color: COLOR_ORANGE
		},{
			title: 'Live Recording',
			start: year + '-'+ month +'-22T03:00:00',
			color: COLOR_BLUE
		},{
			title: 'Announcement',
			start: year + '-'+ month +'-22T15:00:00',
			color: COLOR_RED
		},{
			title: 'Dinner',
			start: year + '-'+ month +'-22T18:00:00'
		},{
			title: 'New Android App Discussion',
			start: year + '-'+ month +'-25T08:00:00',
			end: year + '-'+ month +'-25T10:00:00',
			color: COLOR_RED
		},{
			title: 'Marketing Plan Presentation',
			start: year + '-'+ month +'-25T12:00:00',
			end: year + '-'+ month +'-25T14:00:00',
			color: COLOR_BLUE
		},{
			title: 'Chase due',
			start: year + '-'+ month +'-26T12:00:00',
			color: COLOR_ORANGE
		},{
			title: 'Heartguard',
			start: year + '-'+ month +'-26T08:00:00',
			color: COLOR_ORANGE
		},{
			title: 'Lunch with Richard',
			start: year + '-'+ month +'-28T14:00:00',
			color: COLOR_BLUE
		},{
			title: 'Web Hosting due',
			start: year + '-'+ month +'-30',
			color: COLOR_BLUE
		}]
	});
	
	calendar.render();
};


/* Controller
------------------------------------------------ */
$(document).ready(function() {
	handleRenderFullcalendar();
});